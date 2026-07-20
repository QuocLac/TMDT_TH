using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Areas.Admin.ViewModels.Reviews;
using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Reviews;

namespace WebApplication2.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/Reviews")]
public sealed class ReviewsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IProductReviewService _reviews;
    private readonly ILogger<ReviewsController> _logger;

    public ReviewsController(
        ApplicationDbContext context,
        IProductReviewService reviews,
        ILogger<ReviewsController> logger)
    {
        _context = context;
        _reviews = reviews;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? search,
        ProductReviewStatus? status,
        byte? rating,
        CancellationToken cancellationToken)
    {
        var query = _context.Set<ProductReview>()
            .AsNoTracking()
            .AsQueryable();

        var normalizedSearch = search?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            query = query.Where(review =>
                review.Product.Name.Contains(normalizedSearch)
                || review.OrderItem.Order.Code.Contains(normalizedSearch)
                || review.Customer.FullName.Contains(normalizedSearch)
                || review.Customer.Account.Email.Contains(normalizedSearch));
        }

        if (status.HasValue)
        {
            query = query.Where(review => review.Status == status.Value);
        }

        if (rating is >= 1 and <= 5)
        {
            query = query.Where(review => review.Rating == rating.Value);
        }

        var items = await query
            .OrderByDescending(review => review.SubmittedAt)
            .ThenByDescending(review => review.Id)
            .Take(250)
            .Select(review => new ReviewAdminListItemViewModel
            {
                Id = review.Id,
                ProductName = review.Product.Name,
                OrderCode = review.OrderItem.Order.Code,
                CustomerName = review.Customer.FullName,
                Rating = review.Rating,
                Status = review.Status,
                SubmittedAt = review.SubmittedAt,
                HasReply = review.Reply != null
            })
            .ToArrayAsync(cancellationToken);

        return View(new ReviewAdminIndexViewModel
        {
            Search = normalizedSearch,
            Status = status,
            Rating = rating,
            Items = items
        });
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Details(
        long id,
        CancellationToken cancellationToken)
    {
        var review = await _context.Set<ProductReview>()
            .AsNoTracking()
            .AsSplitQuery()
            .Include(item => item.Product)
            .Include(item => item.OrderItem)
                .ThenInclude(item => item.Order)
            .Include(item => item.Customer)
                .ThenInclude(item => item.Account)
            .Include(item => item.Media)
            .Include(item => item.Reply)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (review is null)
        {
            return NotFound();
        }

        return View(new ReviewAdminDetailsViewModel
        {
            Id = review.Id,
            ProductName = review.Product.Name,
            ProductSlug = review.Product.Slug,
            OrderCode = review.OrderItem.Order.Code,
            CustomerName = review.Customer.FullName,
            CustomerEmail = review.Customer.Account.Email,
            Sku = review.OrderItem.Sku,
            SelectionLabel = review.OrderItem.VariantDescription,
            Rating = review.Rating,
            Title = review.Title,
            Content = review.Content,
            Status = review.Status,
            SubmittedAt = review.SubmittedAt,
            EditedAt = review.EditedAt,
            ModeratedBy = review.ModeratedBy,
            ModeratedAt = review.ModeratedAt,
            ModerationNote = review.ModerationNote,
            RowVersion = Convert.ToBase64String(review.RowVersion),
            Media = review.Media
                .OrderBy(item => item.DisplayOrder)
                .Select(item => new ReviewAdminMediaViewModel
                {
                    Type = item.Type,
                    Url = item.Url
                })
                .ToArray(),
            ReplyContent = review.Reply?.Content,
            RepliedBy = review.Reply?.RepliedBy,
            RepliedAt = review.Reply?.RepliedAt
        });
    }

    [HttpPost("{id:long}/moderate")]
    public async Task<IActionResult> Moderate(
        long id,
        ModerateReviewInput input,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ReviewModerationAction>(
                input.Action,
                ignoreCase: true,
                out var action))
        {
            TempData["ErrorMessage"] = "Thao tác kiểm duyệt không hợp lệ.";
            return RedirectToAction(nameof(Details), new { id });
        }

        try
        {
            await _reviews.ModerateAsync(
                id,
                action,
                DecodeRowVersion(input.RowVersion),
                ResolveActor(),
                input.Note,
                cancellationToken);

            TempData["SuccessMessage"] = action switch
            {
                ReviewModerationAction.Publish =>
                    "Đã cho phép hiển thị đánh giá.",
                ReviewModerationAction.Hide =>
                    "Đã ẩn đánh giá khỏi cửa hàng.",
                ReviewModerationAction.Reject =>
                    "Đã từ chối đánh giá.",
                _ => "Đã cập nhật đánh giá."
            };
        }
        catch (ProductReviewRuleException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            _logger.LogWarning(
                exception,
                "Concurrent review moderation for review {ReviewId}.",
                id);
            TempData["ErrorMessage"] =
                "Đánh giá vừa được xử lý ở nơi khác. Hãy tải lại trang.";
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:long}/reply")]
    public async Task<IActionResult> Reply(
        long id,
        ReplyReviewInput input,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] =
                "Phản hồi cần từ 10 đến 1000 ký tự.";
            return RedirectToAction(nameof(Details), new { id });
        }

        try
        {
            await _reviews.UpsertReplyAsync(
                id,
                ResolveActor(),
                input.Content,
                cancellationToken);

            TempData["SuccessMessage"] =
                "Đã lưu phản hồi công khai.";
        }
        catch (ProductReviewRuleException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    private string ResolveActor() =>
        string.IsNullOrWhiteSpace(User.Identity?.Name)
            ? "Admin"
            : User.Identity.Name;

    private static byte[] DecodeRowVersion(string value)
    {
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return [];
        }
    }
}
