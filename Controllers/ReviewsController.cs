using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Identity;
using WebApplication2.Services.Reviews;
using WebApplication2.ViewModels.Storefront.Reviews;

namespace WebApplication2.Controllers;

[Authorize]
[Route("reviews")]
public sealed class ReviewsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IProductReviewService _reviews;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ReviewsController> _logger;

    public ReviewsController(
        ApplicationDbContext context,
        IProductReviewService reviews,
        TimeProvider timeProvider,
        ILogger<ReviewsController> logger)
    {
        _context = context;
        _reviews = reviews;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Eligible(
        CancellationToken cancellationToken)
    {
        var customerId = RequireCustomerId();
        await ProductReviewExperienceQuery.NormalizeCustomerVisibilityAsync(
            _context,
            _timeProvider,
            customerId,
            orderItemId: null,
            cancellationToken);

        return View(new ReviewableItemsPageViewModel
        {
            Items = await _reviews.GetReviewableItemsAsync(
                customerId,
                cancellationToken)
        });
    }

    [HttpGet("mine")]
    public async Task<IActionResult> Mine(
        CancellationToken cancellationToken)
    {
        var customerId = RequireCustomerId();
        await ProductReviewExperienceQuery.NormalizeCustomerVisibilityAsync(
            _context,
            _timeProvider,
            customerId,
            orderItemId: null,
            cancellationToken);

        return View(new MyProductReviewsPageViewModel
        {
            Items = await _reviews.GetCustomerReviewsAsync(
                customerId,
                cancellationToken)
        });
    }

    [HttpGet("order-item/{orderItemId:int}")]
    public async Task<IActionResult> Edit(
        int orderItemId,
        CancellationToken cancellationToken)
    {
        var customerId = RequireCustomerId();
        await ProductReviewExperienceQuery.NormalizeCustomerVisibilityAsync(
            _context,
            _timeProvider,
            customerId,
            orderItemId,
            cancellationToken);

        var item = await _reviews.GetEditorAsync(
            customerId,
            orderItemId,
            cancellationToken);

        if (item is null)
        {
            return NotFound();
        }

        return View(new ProductReviewEditorPageViewModel
        {
            Item = item,
            Form = new ProductReviewInputModel
            {
                Rating = item.Rating,
                Title = item.Title,
                Content = item.Content,
                RowVersion = item.RowVersion,
                MediaUrls = string.Join(
                    Environment.NewLine,
                    item.Media.Select(media => media.Url))
            }
        });
    }

    [HttpPost("order-item/{orderItemId:int}")]
    public async Task<IActionResult> Edit(
        int orderItemId,
        ProductReviewInputModel form,
        CancellationToken cancellationToken)
    {
        var customerId = RequireCustomerId();
        await ProductReviewExperienceQuery.NormalizeCustomerVisibilityAsync(
            _context,
            _timeProvider,
            customerId,
            orderItemId,
            cancellationToken);

        var item = await _reviews.GetEditorAsync(
            customerId,
            orderItemId,
            cancellationToken);

        if (item is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return View(new ProductReviewEditorPageViewModel
            {
                Item = item,
                Form = form
            });
        }

        try
        {
            var reviewId = await _reviews.SubmitAsync(
                customerId,
                new SubmitProductReviewCommand(
                    orderItemId,
                    form.Rating,
                    form.Title,
                    form.Content,
                    form.RowVersion,
                    ParseMedia(form.MediaUrls)),
                cancellationToken);

            await ProductReviewExperienceQuery.PublishImmediatelyAsync(
                _context,
                _timeProvider,
                reviewId,
                cancellationToken);

            TempData["SuccessMessage"] =
                "Đánh giá đã được đăng công khai ngay lập tức.";

            return RedirectToAction(nameof(Mine));
        }
        catch (ProductReviewRuleException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            _logger.LogWarning(
                exception,
                "Concurrent product review update for order item {OrderItemId}.",
                orderItemId);
            ModelState.AddModelError(
                string.Empty,
                "Đánh giá vừa được thay đổi ở nơi khác. Hãy tải lại trang.");
        }
        catch (DbUpdateException exception)
        {
            _logger.LogWarning(
                exception,
                "Database rejected review for order item {OrderItemId}.",
                orderItemId);
            ModelState.AddModelError(
                string.Empty,
                "Không thể lưu đánh giá. Sản phẩm trong đơn có thể đã được đánh giá.");
        }

        return View(new ProductReviewEditorPageViewModel
        {
            Item = item,
            Form = form
        });
    }

    [AllowAnonymous]
    [HttpGet("product/{productId:int}/feed")]
    public async Task<IActionResult> Feed(
        int productId,
        int page = 1,
        int? rating = null,
        bool mediaOnly = false,
        CancellationToken cancellationToken = default)
    {
        var feed = await ProductReviewExperienceQuery.GetPageAsync(
            _context,
            productId,
            page,
            ProductReviewTransparencyPolicy.InitialPageSize,
            rating,
            mediaOnly,
            User.GetCustomerId(),
            cancellationToken);

        return feed is null
            ? NotFound()
            : PartialView(
                "~/Views/Shared/_ProductReviewFeed.cshtml",
                new ProductReviewFeedViewModel
                {
                    Feed = feed
                });
    }

    [AllowAnonymous]
    [HttpGet("product/{productId:int}")]
    public async Task<IActionResult> Product(
        int productId,
        CancellationToken cancellationToken)
    {
        var product = await _context.Products
            .AsNoTracking()
            .Where(item => item.Id == productId && item.IsActive)
            .Select(item => item.Slug)
            .SingleOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(product))
        {
            return NotFound();
        }

        var productUrl = Url.Action(
            "Details",
            "Products",
            new { slug = product })
            ?? "/";

        return Redirect(productUrl + "#product-reviews");
    }

    [HttpPost("{reviewId:long}/messages")]
    public async Task<IActionResult> AddMessage(
        long reviewId,
        ProductReviewCustomerMessageInput form,
        CancellationToken cancellationToken)
    {
        var customerId = RequireCustomerId();

        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] =
                "Phản hồi cần từ 5 đến 1000 ký tự.";
            return await RedirectToOwnedReviewAsync(
                reviewId,
                customerId,
                cancellationToken);
        }

        try
        {
            var productSlug =
                await ProductReviewConversationWorkflow
                    .AddCustomerMessageAsync(
                        _context,
                        _timeProvider,
                        reviewId,
                        customerId,
                        form.Content,
                        cancellationToken);

            TempData["SuccessMessage"] =
                "Phản hồi của bạn đã được đăng công khai.";

            var productUrl = Url.Action(
                "Details",
                "Products",
                new { slug = productSlug })
                ?? "/";

            return Redirect(
                productUrl + $"#review-{reviewId}");
        }
        catch (ProductReviewRuleException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
            return await RedirectToOwnedReviewAsync(
                reviewId,
                customerId,
                cancellationToken);
        }
    }

    private async Task<IActionResult> RedirectToOwnedReviewAsync(
        long reviewId,
        int customerId,
        CancellationToken cancellationToken)
    {
        var slug =
            await ProductReviewExperienceQuery
                .GetOwnedReviewProductSlugAsync(
                    _context,
                    reviewId,
                    customerId,
                    cancellationToken);

        return string.IsNullOrWhiteSpace(slug)
            ? RedirectToAction(nameof(Mine))
            : Redirect(
                (Url.Action(
                    "Details",
                    "Products",
                    new { slug })
                    ?? "/")
                + $"#review-{reviewId}");
    }

    private int RequireCustomerId() =>
        User.GetCustomerId()
        ?? throw new InvalidOperationException(
            "Authenticated account has no CustomerId claim.");

    private static IReadOnlyList<ProductReviewMediaCommand> ParseMedia(
        string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        return raw
            .Split(
                ['\r', '\n', ','],
                StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(ProductReviewPolicy.MaximumMediaCount + 1)
            .Select(url =>
            {
                var path = Uri.TryCreate(
                    url,
                    UriKind.Absolute,
                    out var uri)
                    ? uri.AbsolutePath
                    : url;

                var extension = Path.GetExtension(path);

                var type = extension.Equals(
                        ".mp4",
                        StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(
                        ".webm",
                        StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(
                        ".mov",
                        StringComparison.OrdinalIgnoreCase)
                        ? ProductReviewMediaType.Video
                        : ProductReviewMediaType.Image;

                return new ProductReviewMediaCommand(type, url);
            })
            .ToArray();
    }
}
