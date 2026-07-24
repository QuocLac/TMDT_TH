using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Areas.Admin.ViewModels.Returns;
using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Commerce.Flows;
using WebApplication2.Services.Commerce.Inventory;
using WebApplication2.Services.Commerce.Returns;
using WebApplication2.Services.Payments.Refunds;
using WebApplication2.Services.Shipping.Internal;

namespace WebApplication2.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/Returns")]
public sealed class ReturnsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IReturnWorkflowService _workflow;
    private readonly IInternalReturnShippingService _shipping;
    private readonly IReturnRefundDestinationService _refundDestination;
    private readonly IInternalRefundService _refunds;
    private readonly ILogger<ReturnsController> _logger;

    public ReturnsController(
        ApplicationDbContext context,
        IReturnWorkflowService workflow,
        IInternalReturnShippingService shipping,
        IReturnRefundDestinationService refundDestination,
        IInternalRefundService refunds,
        ILogger<ReturnsController> logger)
    {
        _context = context;
        _workflow = workflow;
        _shipping = shipping;
        _refundDestination = refundDestination;
        _refunds = refunds;
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
            .AsSplitQuery()
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

        var destination = await _refundDestination.GetAsync(
            request.Id,
            cancellationToken);
        var shipment = request.Shipments
            .Where(item => item.Direction == ShipmentDirection.Return)
            .OrderByDescending(item => item.Id)
            .FirstOrDefault();
        var refundAmount = request.Items.Sum(item => item.RefundAmount);

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
            RefundAmount = refundAmount,
            RefundBankName = destination?.BankName ?? "Chưa có",
            RefundBankCode = destination?.BankCode ?? string.Empty,
            RefundAccountNumber = destination?.MaskedAccountNumber ?? "Chưa có",
            RefundAccountName = destination?.AccountName ?? "Chưa có",
            VietQrUrl = destination is null || refundAmount <= 0
                ? null
                : _refundDestination.BuildQrImageUrl(
                    destination,
                    refundAmount,
                    request.Code),
            Progress = BuildProgress(request.Status),
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
                    TrackingCode = shipment.TrackingCode,
                    CarrierHandoffAt = shipment.CarrierHandoffAt,
                    DeliveredAt = shipment.DeliveredAt,
                    Note = shipment.ProviderReason
                },
            History = request.Order.StatusHistory
                .Where(item =>
                    item.Category == OrderHistoryCategory.Return
                    || item.Code.StartsWith("RETURN_"))
                .OrderByDescending(item => item.OccurredAt)
                .Select(item => new ReturnAdminHistoryViewModel
                {
                    Title = item.Title,
                    Description = item.Description,
                    ChangedBy = item.ChangedBy,
                    OccurredAt = item.OccurredAt
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
            TempData["SuccessMessage"] =
                "Yêu cầu đã được chuyển sang bước xem xét.";
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
                ? "Yêu cầu hoàn trả đã được chấp nhận."
                : "Yêu cầu hoàn trả không được chấp nhận.";
        }
        catch (CommerceFlowException exception)
        {
            SetFlowError(exception);
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:long}/shipping")]
    public async Task<IActionResult> UpdateShipping(
        long id,
        ReturnShippingTransitionInput input,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _shipping.UpdateAsync(
                id,
                input.Action,
                DecodeRowVersion(input.RowVersion),
                ResolveActor(),
                input.Note,
                cancellationToken);
            TempData["SuccessMessage"] = result.Message;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            _logger.LogWarning(
                exception,
                "Concurrent return shipping update for {ReturnRequestId}.",
                id);
            TempData["ErrorMessage"] =
                "Dữ liệu vừa được cập nhật ở nơi khác. Vui lòng tải lại trang.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
        }

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
                "Đã ghi nhận hàng tiếp nhận và bắt đầu kiểm tra.";
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
                "Đã hoàn tất kiểm tra sản phẩm.";
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

    [HttpPost("{id:long}/complete-refund")]
    public async Task<IActionResult> CompleteRefund(
        long id,
        CompleteReturnRefundInput input,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _refunds.CompleteAsync(
                id,
                DecodeRowVersion(input.RowVersion),
                ResolveActor(),
                cancellationToken);
            TempData["SuccessMessage"] = result.Message;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            _logger.LogWarning(
                exception,
                "Concurrent refund completion for return {ReturnRequestId}.",
                id);
            TempData["ErrorMessage"] =
                "Dữ liệu vừa được cập nhật ở nơi khác. Vui lòng tải lại trang.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:long}/close")]
    public async Task<IActionResult> Close(
        long id,
        CompleteReturnRefundInput input,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _refunds.CloseAsync(
                id,
                DecodeRowVersion(input.RowVersion),
                ResolveActor(),
                cancellationToken);
            TempData["SuccessMessage"] = result.Message;
        }
        catch (InvalidOperationException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
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

        TempData["ErrorMessage"] = exception.ErrorCode switch
        {
            "RETURN_REVIEW_REQUIRES_REQUESTED" =>
                "Yêu cầu không còn ở bước tiếp nhận.",
            "RETURN_DECISION_REQUIRES_UNDER_REVIEW" =>
                "Yêu cầu chưa ở bước xem xét.",
            "RETURN_INSPECTION_REQUIRES_WAREHOUSE_RECEIPT" =>
                "Hàng hoàn chưa được ghi nhận tại điểm tiếp nhận.",
            "RETURN_INSPECTION_LINES_INCOMPLETE" =>
                "Vui lòng nhập kết quả cho toàn bộ sản phẩm.",
            _ => "Không thể hoàn tất thao tác từ trạng thái hiện tại. Vui lòng tải lại trang."
        };
    }

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

    private string ResolveActor() =>
        string.IsNullOrWhiteSpace(User.Identity?.Name)
            ? "Admin"
            : User.Identity.Name;

    private string FirstModelError() => ModelState.Values
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

    private static IReadOnlyList<ReturnAdminProgressStepViewModel> BuildProgress(
        ReturnRequestStatus status)
    {
        var current = status switch
        {
            ReturnRequestStatus.Requested => 1,
            ReturnRequestStatus.UnderReview => 2,
            ReturnRequestStatus.Approved
                or ReturnRequestStatus.AwaitingReturnShipment
                or ReturnRequestStatus.AwaitingPickup => 3,
            ReturnRequestStatus.ReturnInTransit => 4,
            ReturnRequestStatus.ReceivedAtWarehouse
                or ReturnRequestStatus.Inspecting => 5,
            ReturnRequestStatus.RefundPending => 6,
            ReturnRequestStatus.Refunded
                or ReturnRequestStatus.Closed => 7,
            ReturnRequestStatus.Rejected
                or ReturnRequestStatus.RejectedAfterInspection
                or ReturnRequestStatus.Cancelled => 2,
            _ => 1
        };

        var titles = new[]
        {
            "Tiếp nhận",
            "Xem xét",
            "Bàn giao",
            "Vận chuyển",
            "Kiểm tra",
            "Hoàn tiền",
            "Hoàn tất"
        };

        return titles.Select((title, index) =>
            new ReturnAdminProgressStepViewModel
            {
                Position = index + 1,
                Title = title,
                IsComplete = index + 1 <= current,
                IsCurrent = index + 1 == current
            }).ToArray();
    }
}
