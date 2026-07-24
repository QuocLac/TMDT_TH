using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Areas.Admin.ViewModels.Reviews;
using WebApplication2.Models;
using WebApplication2.Services.Reviews;

namespace WebApplication2.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/Reviews")]
public sealed class ReviewsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ReviewsController> _logger;

    public ReviewsController(
        ApplicationDbContext context,
        TimeProvider timeProvider,
        ILogger<ReviewsController> logger)
    {
        _context = context;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? search,
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
                SubmittedAt = review.SubmittedAt,
                ConversationCount =
                    review.Messages.Count
                    + (review.Reply == null ? 0 : 1)
            })
            .ToArrayAsync(cancellationToken);

        return View(new ReviewAdminIndexViewModel
        {
            Search = normalizedSearch,
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
            .Include(item => item.Messages)
            .SingleOrDefaultAsync(
                item => item.Id == id,
                cancellationToken);

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
            SubmittedAt = review.SubmittedAt,
            EditedAt = review.EditedAt,
            Media = review.Media
                .OrderBy(item => item.DisplayOrder)
                .Select(item => new ReviewAdminMediaViewModel
                {
                    Type = item.Type,
                    Url = item.Url
                })
                .ToArray(),
            Conversation =
                ProductReviewExperienceQuery.BuildThread(review)
        });
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
                "Phản hồi cần từ 5 đến 1000 ký tự.";
            return RedirectToAction(nameof(Details), new { id });
        }

        try
        {
            await ProductReviewConversationWorkflow.AddAdminMessageAsync(
                _context,
                _timeProvider,
                id,
                ResolveActor(),
                input.Content,
                cancellationToken);

            TempData["SuccessMessage"] =
                "Phản hồi đã được đăng công khai và không thể bị ẩn.";
        }
        catch (ProductReviewRuleException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
        }
        catch (DbUpdateException exception)
        {
            _logger.LogWarning(
                exception,
                "Database rejected review reply for review {ReviewId}.",
                id);
            TempData["ErrorMessage"] =
                "Không thể lưu phản hồi lúc này.";
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    private string ResolveActor() =>
        string.IsNullOrWhiteSpace(User.Identity?.Name)
            ? "Admin"
            : User.Identity.Name;
}
