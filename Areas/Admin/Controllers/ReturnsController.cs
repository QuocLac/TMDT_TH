using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebApplication2.Areas.Admin.ViewModels.Returns;
using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Commerce.Flows;
using WebApplication2.Services.Commerce.Inventory;
using WebApplication2.Services.Commerce.Returns;
using WebApplication2.Services.Shipping;
using WebApplication2.Services.Shipping.Ghn;

namespace WebApplication2.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/Returns")]
public sealed class ReturnsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IReturnWorkflowService _workflow;
    private readonly IReturnShippingService _shipping;
    private readonly GhnShippingOptions _options;
    private readonly ILogger<ReturnsController> _logger;

    public ReturnsController(
        ApplicationDbContext context,
        IReturnWorkflowService workflow,
        IReturnShippingService shipping,
        IOptions<GhnShippingOptions> options,
        ILogger<ReturnsController> logger)
    {
        _context = context;
        _workflow = workflow;
        _shipping = shipping;
        _options = options.Value;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? search,
        ReturnRequestStatus? status,
        CancellationToken cancellationToken)
    {
        var query = _context.ReturnRequests
            .AsNoTracking()
            .Include(item => item.Order)
            .Include(item => item.Items)
            .AsQueryable();

        var normalizedSearch = search?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            query = query.Where(item =>
                item.Code.Contains(normalizedSearch)
                || item.Order.Code.Contains(normalizedSearch)
                || item.Order.CustomerName.Contains(normalizedSearch)
                || item.Order.CustomerEmail.Contains(normalizedSearch));
        }

        if (status.HasValue)
        {
            query = query.Where(item => item.Status == status.Value);
        }

        var items = await query
            .OrderByDescending(item => item.RequestedAt)
            .ThenByDescending(item => item.Id)
            .Take(250)
            .Select(item => new ReturnAdminListItemViewModel
            {
                Id = item.Id,
                Code = item.Code,
                OrderId = item.OrderId,
                OrderCode = item.Order.Code,
                CustomerName = item.Order.CustomerName,
                Status = item.Status,
                RequestedQuantity = item.Items.Sum(line => line.RequestedQuantity),
                ApprovedQuantity = item.Items.Sum(line => line.ApprovedQuantity),
                RefundAmount = item.Items.Sum(line => line.RefundAmount),
                RequestedAt = item.RequestedAt,
                ReturnWindowExpiresAt = item.ReturnWindowExpiresAt
            })
            .ToArrayAsync(cancellationToken);

        return View(new ReturnAdminIndexViewModel
        {
            Items = items,
            Search = normalizedSearch,
            Status = status
        });
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Details(
        long id,
        CancellationToken cancellationToken)
    {
        var request = await _context.ReturnRequests
            .AsNoTracking()
            .Include(item => item.Order)
                .ThenInclude(order => order.StatusHistory)
            .Include(item => item.Items)
                .ThenInclude(item => item.OrderItem)
            .Include(item => item.Evidence)
            .Include(item => item.Shipments)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (request is null)
        {
            return NotFound();
        }

        var shipment = request.Shipments
            .Where(item => item.Direction == ShipmentDirection.Return)
            .OrderByDescending(item => item.Id)
            .FirstOrDefault();

        ViewBag.GhnEnabled = _options.Enabled;
        ViewBag.DefaultServiceTypeId = _options.DefaultServiceTypeId;
        ViewBag.DefaultWeightGram = _options.DefaultWeightGram;
        ViewBag.DefaultLengthCm = _options.DefaultLengthCm;
        ViewBag.DefaultWidthCm = _options.DefaultWidthCm;
        ViewBag.DefaultHeightCm = _options.DefaultHeightCm;

        return View(new ReturnAdminDetailsViewModel
        {
            Id = request.Id,
            Code = request.Code,
            OrderId = request.OrderId,
            OrderCode = request.Order.Code,
            OrderPublicToken = request.Order.PublicToken,
            CustomerName = request.Order.CustomerName,
            CustomerEmail = request.Order.CustomerEmail,
            CustomerPhone = request.Order.CustomerPhone,
            CustomerAddress = BuildAddress(request.Order),
            Status = request.Status,
            ReasonCode = request.ReasonCode,
            ReasonText = request.ReasonText,
            RequestedBy = request.RequestedBy,
            RequestedAt = request.RequestedAt,
            ReturnWindowExpiresAt = request.ReturnWindowExpiresAt,
            ReviewedBy = request.ReviewedBy,
            ReviewedAt = request.ReviewedAt,
            ReviewNote = request.ReviewNote,
            ReceivedAt = request.ReceivedAt,
            InspectedAt = request.InspectedAt,
            InspectionResult = request.InspectionResult,
            RowVersion = Convert.ToBase64String(request.RowVersion),
            Items = request.Items
                .OrderBy(item => item.Id)
                .Select(item => new ReturnAdminItemViewModel
                {
                    Id = item.Id,
                    OrderItemId = item.OrderItemId,
                    ProductName = item.OrderItem.ProductName,
                    Sku = item.OrderItem.Sku,
                    VariantDescription = item.OrderItem.VariantDescription,
                    PurchasedQuantity = item.OrderItem.Quantity,
                    RequestedQuantity = item.RequestedQuantity,
                    ApprovedQuantity = item.ApprovedQuantity,
                    ReceivedQuantity = item.ReceivedQuantity,
                    AcceptedQuantity = item.AcceptedQuantity,
                    RejectedQuantity = item.RejectedQuantity,
                    RestockQuantity = item.RestockQuantity,
                    WriteOffQuantity = item.WriteOffQuantity,
                    ConditionCode = item.ConditionCode,
                    InspectionNote = item.InspectionNote,
                    RefundAmount = item.RefundAmount
                })
                .ToArray(),
            Evidence = request.Evidence
                .OrderBy(item => item.CreatedAt)
                .Select(item => new ReturnAdminEvidenceViewModel
                {
                    Type = item.Type,
                    Url = item.Url,
                    Caption = item.Caption,
                    CreatedAt = item.CreatedAt
                })
                .ToArray(),
            Shipment = shipment is null
                ? null
                : new ReturnAdminShipmentViewModel
                {
                    Id = shipment.Id,
                    Status = shipment.Status,
                    ProviderStatus = shipment.ProviderStatus,
                    TrackingCode = shipment.TrackingCode,
                    Fee = shipment.Fee,
                    WeightGram = shipment.WeightGram,
                    LengthCm = shipment.LengthCm,
                    WidthCm = shipment.WidthCm,
                    HeightCm = shipment.HeightCm,
                    CarrierHandoffAt = shipment.CarrierHandoffAt,
                    DeliveredAt = shipment.DeliveredAt,
                    LastSyncedAt = shipment.LastSyncedAt,
                    ProviderReason = shipment.ProviderReason
                },
            History = request.Order.StatusHistory
                .Where(item =>
                    item.Category == OrderHistoryCategory.Return
                    || item.Code.StartsWith("RETURN_"))
                .OrderByDescending(item => item.OccurredAt)
                .Select(item => new ReturnAdminHistoryViewModel
                {
                    Code = item.Code,
                    Title = item.Title,
                    Description = item.Description,
                    ChangedBy = item.ChangedBy,
                    OccurredAt = item.OccurredAt,
                    CorrelationId = item.CorrelationId
                })
                .ToArray()
        });
    }

    [HttpPost("{id:long}/start-review")]
    public async Task<IActionResult> StartReview(
        long id,
        StartReturnReviewInput input,
        CancellationToken cancellationToken)
    {
        try
        {
            await _workflow.StartReviewAsync(
                new StartReturnReviewCommand(
                    id,
                    DecodeRowVersion(input.RowVersion),
                    ResolveActor(),
                    input.Note ?? string.Empty),
                cancellationToken);
            TempData["SuccessMessage"] = "Đã chuyển yêu cầu sang trạng thái đang duyệt.";
        }
        catch (CommerceFlowException exception)
        {
            SetFlowError(exception);
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:long}/decide")]
    public async Task<IActionResult> Decide(
        long id,
        DecideReturnInput input,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = FirstModelError();
            return RedirectToAction(nameof(Details), new { id });
        }

        try
        {
            await _workflow.DecideAsync(
                new DecideReturnRequestCommand(
                    id,
                    input.Approve,
                    DecodeRowVersion(input.RowVersion),
                    ResolveActor(),
                    input.Note,
                    input.Items.Select(item => new ReturnApprovalLineCommand(
                        item.ReturnItemId,
                        item.ApprovedQuantity)).ToArray()),
                cancellationToken);
            TempData["SuccessMessage"] = input.Approve
                ? "Đã phê duyệt yêu cầu hoàn trả."
                : "Đã từ chối yêu cầu hoàn trả.";
        }
        catch (CommerceFlowException exception)
        {
            SetFlowError(exception);
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet("{id:long}/services")]
    public async Task<IActionResult> Services(
        long id,
        CancellationToken cancellationToken)
    {
        return ProviderResponse(
            await _shipping.GetServicesAsync(id, cancellationToken));
    }

    [HttpPost("{id:long}/quote")]
    public async Task<IActionResult> Quote(
        long id,
        [FromBody] QueueReturnShipmentInput input,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new
            {
                success = false,
                message = "Thông số kiện hàng không hợp lệ."
            });
        }

        return ProviderResponse(
            await _shipping.QuoteAsync(
                id,
                ToShippingInput(input),
                cancellationToken));
    }

    [HttpPost("{id:long}/queue-shipment")]
    public async Task<IActionResult> QueueShipment(
        long id,
        QueueReturnShipmentInput input,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = FirstModelError();
            return RedirectToAction(nameof(Details), new { id });
        }

        try
        {
            var result = await _shipping.QueueCreateAsync(
                id,
                ToShippingInput(input),
                DecodeRowVersion(input.RowVersion),
                ResolveActor(),
                cancellationToken);
            TempData["SuccessMessage"] = result.Message;
        }
        catch (CommerceFlowException exception)
        {
            SetFlowError(exception);
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:long}/sync-shipment")]
    public async Task<IActionResult> SyncShipment(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await _shipping.SyncAsync(
            id,
            ResolveActor(),
            cancellationToken);
        TempData[result.Success ? "SuccessMessage" : "ErrorMessage"] =
            result.Success ? "Đã đồng bộ return shipment từ GHN." : result.Message;

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:long}/begin-inspection")]
    public async Task<IActionResult> BeginInspection(
        long id,
        BeginReturnInspectionInput input,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = FirstModelError();
            return RedirectToAction(nameof(Details), new { id });
        }

        try
        {
            await _workflow.BeginInspectionAsync(
                new BeginReturnInspectionCommand(
                    id,
                    DecodeRowVersion(input.RowVersion),
                    ResolveActor(),
                    input.Note ?? string.Empty,
                    input.Items.Select(item => new ReturnReceiptLineCommand(
                        item.ReturnItemId,
                        item.ReceivedQuantity)).ToArray()),
                cancellationToken);
            TempData["SuccessMessage"] =
                "Đã ghi nhận số lượng kho tiếp nhận và bắt đầu kiểm định.";
        }
        catch (CommerceFlowException exception)
        {
            SetFlowError(exception);
        }
        catch (InventoryValidationException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
        }
        catch (InventoryConflictException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:long}/complete-inspection")]
    public async Task<IActionResult> CompleteInspection(
        long id,
        CompleteReturnInspectionInput input,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = FirstModelError();
            return RedirectToAction(nameof(Details), new { id });
        }

        try
        {
            await _workflow.CompleteInspectionAsync(
                new CompleteReturnInspectionCommand(
                    id,
                    DecodeRowVersion(input.RowVersion),
                    ResolveActor(),
                    input.Note,
                    input.IdempotencyKey,
                    input.Items.Select(item => new ReturnInspectionLineCommand(
                        item.ReturnItemId,
                        item.AcceptedQuantity,
                        item.RejectedQuantity,
                        item.RestockQuantity,
                        item.WriteOffQuantity,
                        item.ConditionCode,
                        item.Note)).ToArray()),
                cancellationToken);
            TempData["SuccessMessage"] =
                "Đã hoàn tất kiểm định; yêu cầu hợp lệ đã chuyển sang RefundPending.";
        }
        catch (CommerceFlowException exception)
        {
            SetFlowError(exception);
        }
        catch (InventoryValidationException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
        }
        catch (InventoryConflictException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    private IActionResult ProviderResponse<T>(ShippingOperationResult<T> result)
    {
        if (result.Success)
        {
            return Ok(new { success = true, data = result.Data });
        }

        return StatusCode(
            result.Retryable
                ? StatusCodes.Status503ServiceUnavailable
                : StatusCodes.Status422UnprocessableEntity,
            new
            {
                success = false,
                errorCode = result.ErrorCode,
                message = result.Message,
                retryable = result.Retryable
            });
    }

    private void SetFlowError(CommerceFlowException exception)
    {
        _logger.LogWarning(
            exception,
            "Return admin flow failed. ErrorCode={ErrorCode}; Stage={Stage}; Aggregate={AggregateType}:{AggregateId}; CorrelationId={CorrelationId}",
            exception.ErrorCode,
            exception.FlowContext.Stage,
            exception.FlowContext.AggregateType,
            exception.FlowContext.AggregateId,
            exception.FlowContext.CorrelationId);

        TempData["ErrorMessage"] =
            $"{exception.DetailMessage} (Mã: {exception.ErrorCode}; Stage: {exception.FlowContext.Stage}; Correlation: {exception.FlowContext.CorrelationId})";
    }

    private static byte[] DecodeRowVersion(string value)
    {
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException exception)
        {
            throw new BusinessRuleViolationException(
                "RETURN_ROW_VERSION_INVALID",
                "RowVersion của return request không hợp lệ.",
                new CommerceFlowContext(
                    "ReturnAdmin",
                    CommerceFlowStage.ValidateConcurrency,
                    nameof(ReturnRequest),
                    "unknown",
                    null,
                    "DecodeRowVersion",
                    Guid.NewGuid().ToString("N"),
                    null,
                    new Dictionary<string, string>()),
                exception);
        }
    }

    private static ReturnShippingExecutionInput ToShippingInput(
        QueueReturnShipmentInput input) =>
        new(
            input.ServiceId,
            input.ServiceTypeId,
            input.WeightGram,
            input.LengthCm,
            input.WidthCm,
            input.HeightCm,
            input.Note);

    private string ResolveActor() =>
        string.IsNullOrWhiteSpace(User.Identity?.Name)
            ? "Admin"
            : User.Identity.Name;

    private string FirstModelError() =>
        ModelState.Values
            .SelectMany(item => item.Errors)
            .Select(item => item.ErrorMessage)
            .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item))
        ?? "Dữ liệu chưa hợp lệ.";

    private static string BuildAddress(Order order) =>
        string.Join(", ", new[]
        {
            order.ShippingAddressLine,
            order.ShippingWard,
            order.ShippingDistrict,
            order.ShippingCity
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
}
