using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Identity;
using WebApplication2.Services.Reviews;
using WebApplication2.ViewModels.Storefront.Reviews;

namespace WebApplication2.Controllers;

[Authorize]
[Route("reviews")]
public sealed class ReviewsController : Controller
{
    private readonly IProductReviewService _reviews;
    private readonly ILogger<ReviewsController> _logger;

    public ReviewsController(
        IProductReviewService reviews,
        ILogger<ReviewsController> logger)
    {
        _reviews = reviews;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Eligible(
        CancellationToken cancellationToken)
    {
        return View(new ReviewableItemsPageViewModel
        {
            Items = await _reviews.GetReviewableItemsAsync(
                RequireCustomerId(),
                cancellationToken)
        });
    }

    [HttpGet("mine")]
    public async Task<IActionResult> Mine(
        CancellationToken cancellationToken)
    {
        return View(new MyProductReviewsPageViewModel
        {
            Items = await _reviews.GetCustomerReviewsAsync(
                RequireCustomerId(),
                cancellationToken)
        });
    }

    [HttpGet("order-item/{orderItemId:int}")]
    public async Task<IActionResult> Edit(
        int orderItemId,
        CancellationToken cancellationToken)
    {
        var item = await _reviews.GetEditorAsync(
            RequireCustomerId(),
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
        var item = await _reviews.GetEditorAsync(
            RequireCustomerId(),
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
            await _reviews.SubmitAsync(
                RequireCustomerId(),
                new SubmitProductReviewCommand(
                    orderItemId,
                    form.Rating,
                    form.Title,
                    form.Content,
                    form.RowVersion,
                    ParseMedia(form.MediaUrls)),
                cancellationToken);

            TempData["SuccessMessage"] =
                "Đánh giá đã được gửi và đang chờ kiểm duyệt.";

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
    [HttpGet("product/{productId:int}")]
    public async Task<IActionResult> Product(
        int productId,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var result = await _reviews.GetProductPageAsync(
            productId,
            page,
            pageSize: 20,
            cancellationToken);

        return result is null
            ? NotFound()
            : View(new ProductReviewPublicPageViewModel
            {
                Page = result
            });
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
