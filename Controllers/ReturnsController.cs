using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Commerce.Flows;
using WebApplication2.Services.Commerce.Returns;
using WebApplication2.Services.Identity;
using WebApplication2.ViewModels.Storefront.Returns;

namespace WebApplication2.Controllers;

[Authorize]
[Route("returns")]
public sealed class ReturnsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IReturnWorkflowService _workflow;
    private readonly ILogger<ReturnsController> _logger;

    public ReturnsController(
        ApplicationDbContext context,
        IReturnWorkflowService workflow,
        ILogger<ReturnsController> logger)
    {
        _context = context;
        _workflow = workflow;
        _logger = logger;
    }

    [HttpGet("request/{publicToken:guid}")]
    public async Task<IActionResult> RequestReturn(
        Guid publicToken,
        CancellationToken cancellationToken)
    {
        if (!await OwnsOrderAsync(publicToken, cancellationToken))
        {
            return NotFound();
        }

        var eligibility = await _workflow.GetEligibilityAsync(
            publicToken,
            cancellationToken);
        if (eligibility is null)
        {
            return NotFound();
        }

        return View("Request", BuildRequestPage(
            eligibility,
            new CreateReturnRequestInput
            {
                OrderPublicToken = publicToken,
                IdempotencyKey = Guid.NewGuid().ToString("N"),
                Lines = eligibility.Items
                    .OrderBy(item => item.OrderItemId)
                    .Select(item => new CreateReturnLineInput
                    {
                        OrderItemId = item.OrderItemId,
                        Quantity = item.PurchasedQuantity
                    })
                    .ToList()
            },
            null));
    }

    [HttpPost("request")]
    public async Task<IActionResult> RequestReturn(
        [Bind(Prefix = "Form")] CreateReturnRequestInput input,
        CancellationToken cancellationToken)
    {
        if (!await OwnsOrderAsync(input.OrderPublicToken, cancellationToken))
        {
            return NotFound();
        }

        var eligibility = await _workflow.GetEligibilityAsync(
            input.OrderPublicToken,
            cancellationToken);
        if (eligibility is null)
        {
            return NotFound();
        }

        var canReturnWholeOrder = eligibility.IsEligible
            && eligibility.Items.Count > 0
            && eligibility.Items.All(item =>
                item.PurchasedQuantity > 0
                && item.CancelledQuantity == 0
                && item.ReservedReturnQuantity == 0
                && item.AvailableQuantity == item.PurchasedQuantity);

        if (!canReturnWholeOrder)
        {
            ModelState.AddModelError(
                string.Empty,
                eligibility.IsEligible
                    ? "FastBuy chỉ hỗ trợ hoàn trả toàn bộ đơn hàng. Đơn có sản phẩm đã hủy, đã hoàn hoặc đang nằm trong yêu cầu hoàn trả khác sẽ không thể tạo yêu cầu mới."
                    : eligibility.Message);
        }

        var wholeOrderLines = eligibility.Items
            .OrderBy(item => item.OrderItemId)
            .Select(item => new ReturnRequestLineCommand(
                item.OrderItemId,
                item.PurchasedQuantity))
            .ToArray();

        if (!ModelState.IsValid)
        {
            return View("Request", BuildRequestPage(
                eligibility,
                input,
                FirstModelError()));
        }

        try
        {
            var request = await _workflow.CreateRequestAsync(
                new CreateReturnRequestCommand(
                    input.OrderPublicToken,
                    input.ReasonCode,
                    input.ReasonText,
                    User.Identity?.Name ?? eligibility.CustomerEmail,
                    input.IdempotencyKey,
                    wholeOrderLines,
                    ParseEvidence(input.EvidenceUrls)),
                cancellationToken);

            return RedirectToAction(
                nameof(Confirmation),
                new
                {
                    returnCode = request.Code,
                    publicToken = input.OrderPublicToken
                });
        }
        catch (CommerceFlowException exception)
        {
            LogFlow(exception);
            ModelState.AddModelError(string.Empty, exception.DetailMessage);
            return View("Request", BuildRequestPage(
                eligibility,
                input,
                exception.DetailMessage));
        }
    }

    [HttpGet("{returnCode}/confirmation")]
    public async Task<IActionResult> Confirmation(
        string returnCode,
        Guid publicToken,
        CancellationToken cancellationToken)
    {
        var customerId = RequireCustomerId();
        var request = await _context.ReturnRequests
            .AsNoTracking()
            .Include(item => item.Order)
            .Include(item => item.Items)
                .ThenInclude(item => item.OrderItem)
            .Include(item => item.Shipments)
            .SingleOrDefaultAsync(
                item => item.Code == returnCode
                    && item.Order.PublicToken == publicToken
                    && item.Order.CustomerId == customerId,
                cancellationToken);

        if (request is null)
        {
            return NotFound();
        }

        var shipment = request.Shipments
            .Where(item => item.Direction == ShipmentDirection.Return)
            .OrderByDescending(item => item.Id)
            .FirstOrDefault();

        return View(new ReturnConfirmationViewModel
        {
            ReturnCode = request.Code,
            OrderCode = request.Order.Code,
            OrderPublicToken = request.Order.PublicToken,
            Status = request.Status,
            ReasonCode = request.ReasonCode,
            ReasonText = request.ReasonText,
            RequestedAt = request.RequestedAt,
            ReturnWindowExpiresAt = request.ReturnWindowExpiresAt,
            TrackingCode = shipment?.TrackingCode,
            Items = request.Items
                .OrderBy(item => item.Id)
                .Select(item => new ReturnConfirmationLineViewModel
                {
                    ProductName = item.OrderItem.ProductName,
                    Sku = item.OrderItem.Sku,
                    VariantDescription = item.OrderItem.VariantDescription,
                    RequestedQuantity = item.RequestedQuantity,
                    ApprovedQuantity = item.ApprovedQuantity,
                    ReceivedQuantity = item.ReceivedQuantity,
                    RefundAmount = item.RefundAmount
                })
                .ToArray()
        });
    }

    private async Task<bool> OwnsOrderAsync(
        Guid publicToken,
        CancellationToken cancellationToken)
    {
        if (publicToken == Guid.Empty)
        {
            return false;
        }

        var customerId = RequireCustomerId();
        return await _context.Orders
            .AsNoTracking()
            .AnyAsync(
                item => item.PublicToken == publicToken
                    && item.CustomerId == customerId,
                cancellationToken);
    }

    private int RequireCustomerId() => User.GetCustomerId()
        ?? throw new InvalidOperationException(
            "Authenticated account has no CustomerId claim.");

    private static IReadOnlyList<ReturnEvidenceCommand> ParseEvidence(
        string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        return raw
            .Split(
                ['\r', '\n', ','],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(9)
            .Select(url =>
            {
                var extension = Path.GetExtension(
                    Uri.TryCreate(url, UriKind.Absolute, out var uri)
                        ? uri.AbsolutePath
                        : url);
                var type = extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".webm", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".mov", StringComparison.OrdinalIgnoreCase)
                    ? ReturnEvidenceType.Video
                    : ReturnEvidenceType.Image;
                return new ReturnEvidenceCommand(type, url, null);
            })
            .ToArray();
    }

    private static ReturnRequestPageViewModel BuildRequestPage(
        ReturnEligibilitySnapshot eligibility,
        CreateReturnRequestInput form,
        string? error) => new()
    {
        Eligibility = eligibility,
        Form = form,
        ErrorMessage = error
    };

    private string FirstModelError() => ModelState.Values
        .SelectMany(item => item.Errors)
        .Select(item => item.ErrorMessage)
        .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item))
        ?? "Yêu cầu hoàn trả chưa hợp lệ.";

    private void LogFlow(CommerceFlowException exception)
    {
        _logger.LogWarning(
            exception,
            "Return storefront flow failed. ErrorCode={ErrorCode}; Stage={Stage}; Aggregate={AggregateType}:{AggregateId}; CorrelationId={CorrelationId}",
            exception.ErrorCode,
            exception.FlowContext.Stage,
            exception.FlowContext.AggregateType,
            exception.FlowContext.AggregateId,
            exception.FlowContext.CorrelationId);
    }
}
