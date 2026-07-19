using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Areas.Admin.ViewModels.CatalogReadiness;
using WebApplication2.Services.Catalog;

namespace WebApplication2.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/CatalogPublishing")]
public sealed class CatalogPublishingController : Controller
{
    private readonly IProductPublicationService _publication;
    private readonly ILogger<CatalogPublishingController> _logger;

    public CatalogPublishingController(
        IProductPublicationService publication,
        ILogger<CatalogPublishingController> logger)
    {
        _publication = publication;
        _logger = logger;
    }

    [HttpPost("{productId:int}/publish")]
    public async Task<IActionResult> Publish(
        int productId,
        CatalogPublicationReturnInput input,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _publication.SetVisibilityAsync(
                productId,
                publish: true,
                cancellationToken);

            if (result.Success)
            {
                TempData["SuccessMessage"] =
                    $"Đã hiển thị sản phẩm “{result.ProductName}”.";
            }
            else
            {
                TempData["ErrorMessage"] =
                    BuildFailureMessage(result.ProductName, result.Issues);
            }
        }
        catch (DbUpdateConcurrencyException exception)
        {
            _logger.LogWarning(
                exception,
                "Concurrent product publication for product {ProductId}.",
                productId);
            TempData["ErrorMessage"] =
                "Dữ liệu sản phẩm vừa thay đổi. Hãy tải lại trang và thử lại.";
        }
        catch (DbUpdateException exception)
        {
            _logger.LogWarning(
                exception,
                "Database rejected publication for product {ProductId}.",
                productId);
            TempData["ErrorMessage"] =
                "Không thể hiển thị sản phẩm do dữ liệu chưa nhất quán.";
        }

        return RedirectToReadiness(input);
    }

    [HttpPost("{productId:int}/hide")]
    public async Task<IActionResult> Hide(
        int productId,
        CatalogPublicationReturnInput input,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _publication.SetVisibilityAsync(
                productId,
                publish: false,
                cancellationToken);

            TempData[result.Success
                ? "SuccessMessage"
                : "ErrorMessage"] = result.Success
                ? $"Đã ẩn sản phẩm “{result.ProductName}”."
                : BuildFailureMessage(result.ProductName, result.Issues);
        }
        catch (DbUpdateException exception)
        {
            _logger.LogWarning(
                exception,
                "Database rejected hide operation for product {ProductId}.",
                productId);
            TempData["ErrorMessage"] =
                "Không thể ẩn sản phẩm lúc này.";
        }

        return RedirectToReadiness(input);
    }

    [HttpPost("bulk")]
    public async Task<IActionResult> Bulk(
        CatalogPublicationBulkInput input,
        CancellationToken cancellationToken)
    {
        var publish = string.Equals(
            input.Operation,
            "publish",
            StringComparison.OrdinalIgnoreCase);

        var hide = string.Equals(
            input.Operation,
            "hide",
            StringComparison.OrdinalIgnoreCase);

        if (!publish && !hide)
        {
            TempData["ErrorMessage"] = "Thao tác hàng loạt không hợp lệ.";
            return RedirectToReadiness(input);
        }

        if (input.ProductIds.Count == 0)
        {
            TempData["ErrorMessage"] =
                "Vui lòng chọn ít nhất một sản phẩm.";
            return RedirectToReadiness(input);
        }

        try
        {
            var result = await _publication.SetVisibilityBatchAsync(
                input.ProductIds,
                publish,
                cancellationToken);

            var actionName = publish ? "hiển thị" : "ẩn";

            if (result.ChangedCount > 0)
            {
                TempData["SuccessMessage"] =
                    $"Đã {actionName} {result.ChangedCount} sản phẩm.";
            }

            if (result.FailedCount > 0)
            {
                var failures = result.Items
                    .Where(item => !item.Success)
                    .Take(3)
                    .Select(item =>
                        $"{item.ProductName}: "
                        + string.Join(" ", item.Issues.Take(2)))
                    .ToArray();

                TempData["ErrorMessage"] =
                    $"{result.FailedCount} sản phẩm chưa thể {actionName}. "
                    + string.Join(" | ", failures);
            }
        }
        catch (DbUpdateException exception)
        {
            _logger.LogWarning(
                exception,
                "Database rejected bulk catalog publication.");
            TempData["ErrorMessage"] =
                "Không thể hoàn tất thao tác hàng loạt.";
        }

        return RedirectToReadiness(input);
    }

    private IActionResult RedirectToReadiness(
        CatalogPublicationReturnInput input) =>
        RedirectToAction(
            "Index",
            "CatalogReadiness",
            new
            {
                query = input.Query?.Trim(),
                status = input.Status?.Trim(),
                page = Math.Max(1, input.Page)
            });

    private static string BuildFailureMessage(
        string productName,
        IReadOnlyList<string> issues)
    {
        var details = issues.Count == 0
            ? "Dữ liệu sản phẩm chưa đáp ứng điều kiện hiển thị."
            : string.Join(" ", issues.Take(5));

        return $"Chưa thể hiển thị “{productName}”. {details}";
    }
}
