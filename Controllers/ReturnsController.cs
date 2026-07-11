using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Commerce.Flows;
using WebApplication2.Services.Commerce.Returns;
using WebApplication2.ViewModels.Storefront.Returns;

namespace WebApplication2.Controllers;

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
                Lines = eligibility.Items.Select(item => new CreateReturnLineInput
                {
                    OrderItemId = item.OrderItemId,
                    Quantity = 0
                }).ToList()
            },
            null));
    }

    [HttpPost("request")]
    public async Task<IActionResult> RequestReturn(
        [Bind(Prefix = "Form")] CreateReturnRequestInput input,
        CancellationToken cancellationToken)
    {
        var eligibility = await _workflow.GetEligibilityAsync(
            input.OrderPublicToken,
            cancellationToken);
        if (eligibility is null)
        {
            return NotFound();
        }

        if (!eligibility.IsEligible)
        {
            ModelState.AddModelError(
                string.Empty,
                eligibility.Message);
        }

        var selectedLines = input.Lines
            .Where(item => item.Quantity > 0)
            .ToArray();
        if (selectedLines.Length == 0)
        {
            ModelState.AddModelError(
                string.Empty,
                "Hãy chọn ít nhất một sản phẩm và số lượng cần hoàn trả.");
        }

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
                    eligibility.CustomerEmail,
                    input.IdempotencyKey,
                    selectedLines.Select(item => new ReturnRequestLineCommand(
                        item.OrderItemId,
                        item.Quantity)).ToArray(),
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
        var request = await _context.ReturnRequests
            .AsNoTracking()
            .Include(item => item.Order)
            .Include(item => item.Items)
                .ThenInclude(item => item.OrderItem)
            .Include(item => item.Shipments)
            .SingleOrDefaultAsync(
                item => item.Code == returnCode
                    && item.Order.PublicToken == publicToken,
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
        string? error) =>
        new()
        {
            Eligibility = eligibility,
            Form = form,
            ErrorMessage = error
        };

    private string FirstModelError() =>
        ModelState.Values
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
