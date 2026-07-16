using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Commerce.Flows;
using WebApplication2.Services.Commerce.Inventory;

namespace WebApplication2.Services.Commerce.Returns;

public sealed class ReturnWorkflowService : IReturnWorkflowService
{
    private const int ActorMaxLength = 100;
    private const int ReasonCodeMaxLength = 50;
    private const int ReasonTextMaxLength = 500;
    private const int NoteMaxLength = 1000;
    private const int IdempotencyKeyMaxLength = 128;
    private const int EvidenceUrlMaxLength = 500;
    private const int EvidenceCaptionMaxLength = 200;
    private const int MaximumEvidenceCount = 8;

    private static readonly HashSet<string> AllowedReasonCodes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "ChangedMind",
            "WrongSize",
            "Damaged",
            "WrongItem",
            "MissingParts",
            "Other"
        };

    private readonly ApplicationDbContext _context;
    private readonly IReturnCodeGenerator _codeGenerator;
    private readonly IReturnInventoryService _inventoryService;
    private readonly TimeProvider _timeProvider;

    public ReturnWorkflowService(
        ApplicationDbContext context,
        IReturnCodeGenerator codeGenerator,
        IReturnInventoryService inventoryService,
        TimeProvider timeProvider)
    {
        _context = context;
        _codeGenerator = codeGenerator;
        _inventoryService = inventoryService;
        _timeProvider = timeProvider;
    }

    public async Task<ReturnEligibilitySnapshot?> GetEligibilityAsync(
        Guid orderPublicToken,
        CancellationToken cancellationToken)
    {
        if (orderPublicToken == Guid.Empty)
        {
            return null;
        }

        var order = await LoadOrderForEligibilityAsync(
            orderPublicToken,
            tracked: false,
            cancellationToken);
        if (order is null)
        {
            return null;
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var outbound = ReturnPolicy.GetDeliveredOutboundShipment(order);
        DateTime? deadline = outbound?.DeliveredAt is DateTime deliveredAt
            ? ReturnPolicy.GetDeadlineUtc(deliveredAt)
            : null;
        var error = GetEligibilityError(order, outbound, nowUtc);
        var availability = BuildAvailability(order, excludeReturnRequestId: null);

        return new ReturnEligibilitySnapshot(
            order.Id,
            order.PublicToken,
            order.Code,
            order.CustomerName,
            order.CustomerEmail,
            order.OrderStatus,
            order.FulfillmentStatus,
            outbound?.DeliveredAt,
            deadline,
            error is null && availability.Values.Any(item => item.AvailableQuantity > 0),
            error?.ErrorCode
                ?? (availability.Values.Any(item => item.AvailableQuantity > 0)
                    ? null
                    : "RETURN_NO_QUANTITY_AVAILABLE"),
            error?.Message
                ?? (availability.Values.Any(item => item.AvailableQuantity > 0)
                    ? $"Đơn đủ điều kiện gửi yêu cầu trong {ReturnPolicy.ReturnWindowDays} ngày kể từ lúc GHN giao thành công."
                    : "Toàn bộ số lượng đã được hủy hoặc đã nằm trong yêu cầu hoàn trả khác."),
            order.Items
                .OrderBy(item => item.Id)
                .Select(item =>
                {
                    var state = availability[item.Id];
                    return new ReturnEligibilityLine(
                        item.Id,
                        item.ProductName,
                        item.Sku,
                        item.VariantDescription,
                        item.ImageUrl,
                        item.Quantity,
                        state.CancelledQuantity,
                        state.ReservedReturnQuantity,
                        state.AvailableQuantity,
                        item.UnitPrice,
                        item.LineTotal);
                })
                .ToArray());
    }

    public Task<ReturnRequest> CreateRequestAsync(
        CreateReturnRequestCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var tracker = new CommerceFlowTracker(
            "ReturnRequest",
            nameof(Order),
            command.OrderPublicToken.ToString("N"),
            "CreateReturnRequest",
            idempotencyKey: command.IdempotencyKey);

        tracker.MoveTo(CommerceFlowStage.ValidateInput);
        var reasonCode = NormalizeRequired(
            command.ReasonCode,
            ReasonCodeMaxLength,
            "Mã lý do",
            "RETURN_REASON_CODE_REQUIRED",
            tracker);
        if (!AllowedReasonCodes.Contains(reasonCode))
        {
            throw new BusinessRuleViolationException(
                "RETURN_REASON_CODE_INVALID",
                $"Mã lý do '{reasonCode}' không nằm trong danh sách cho phép.",
                tracker.Snapshot());
        }

        var reasonText = NormalizeRequired(
            command.ReasonText,
            ReasonTextMaxLength,
            "Mô tả lý do",
            "RETURN_REASON_TEXT_REQUIRED",
            tracker);
        var requestedBy = NormalizeRequired(
            command.RequestedBy,
            ActorMaxLength,
            "Người yêu cầu",
            "RETURN_REQUESTED_BY_REQUIRED",
            tracker);
        var idempotencyKey = NormalizeRequired(
            command.IdempotencyKey,
            IdempotencyKeyMaxLength,
            "Idempotency key",
            "RETURN_IDEMPOTENCY_KEY_REQUIRED",
            tracker);
        var normalizedLines = NormalizeRequestLines(command.Lines, tracker);
        var normalizedEvidence = NormalizeEvidence(command.Evidence, tracker);

        if (reasonCode is "Damaged" or "WrongItem" or "MissingParts"
            && normalizedEvidence.Count == 0)
        {
            throw new BusinessRuleViolationException(
                "RETURN_EVIDENCE_REQUIRED",
                $"Lý do {reasonCode} yêu cầu ít nhất một ảnh hoặc video làm bằng chứng.",
                tracker.Snapshot());
        }

        return CommerceFlowTransaction.ExecuteAsync(
            _context,
            tracker,
            async token =>
            {
                tracker.MoveTo(CommerceFlowStage.Deduplicate);
                var existing = await _context.ReturnRequests
                    .Include(item => item.Items)
                    .Include(item => item.Evidence)
                    .Include(item => item.Order)
                    .SingleOrDefaultAsync(
                        item => item.IdempotencyKey == idempotencyKey,
                        token);

                if (existing is not null)
                {
                    EnsureSameRequestPayload(
                        existing,
                        command.OrderPublicToken,
                        reasonCode,
                        reasonText,
                        normalizedLines,
                        normalizedEvidence,
                        tracker);
                    return existing;
                }

                tracker.MoveTo(CommerceFlowStage.LoadAggregate);
                var order = await LoadOrderForEligibilityAsync(
                    command.OrderPublicToken,
                    tracked: true,
                    token)
                    ?? throw new BusinessRuleViolationException(
                        "RETURN_ORDER_NOT_FOUND",
                        "Không tìm thấy đơn hàng theo public token.",
                        tracker.Snapshot());

                tracker.AddMetadata("OrderId", order.Id)
                    .AddMetadata("OrderCode", order.Code)
                    .AddMetadata("OrderStatus", order.OrderStatus)
                    .AddMetadata("FulfillmentStatus", order.FulfillmentStatus);

                var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
                var outbound = ReturnPolicy.GetDeliveredOutboundShipment(order);
                var eligibilityError = GetEligibilityError(order, outbound, nowUtc);
                if (eligibilityError is not null)
                {
                    throw new BusinessRuleViolationException(
                        eligibilityError.Value.ErrorCode,
                        eligibilityError.Value.Message,
                        tracker.MoveTo(CommerceFlowStage.ValidateBusinessRules).Snapshot());
                }

                var availability = BuildAvailability(order, excludeReturnRequestId: null);
                var orderItems = order.Items.ToDictionary(item => item.Id);

                foreach (var line in normalizedLines)
                {
                    if (!orderItems.TryGetValue(line.OrderItemId, out var orderItem))
                    {
                        throw new BusinessRuleViolationException(
                            "RETURN_ITEM_NOT_IN_ORDER",
                            $"Order item {line.OrderItemId} không thuộc đơn hàng {order.Code}.",
                            tracker.Snapshot());
                    }

                    var available = availability[orderItem.Id].AvailableQuantity;
                    if (line.Quantity > available)
                    {
                        throw new BusinessRuleViolationException(
                            "RETURN_QUANTITY_EXCEEDED",
                            $"Sản phẩm {orderItem.Sku} chỉ còn {available} đơn vị có thể hoàn trả, nhưng yêu cầu {line.Quantity}.",
                            tracker.Snapshot());
                    }
                }

                var code = await _codeGenerator.GenerateAsync(token);
                var request = new ReturnRequest
                {
                    OrderId = order.Id,
                    Code = code,
                    Status = ReturnRequestStatus.Requested,
                    ReasonCode = reasonCode,
                    ReasonText = reasonText,
                    RequestedBy = requestedBy,
                    IdempotencyKey = idempotencyKey,
                    RequestedAt = nowUtc,
                    ReturnWindowExpiresAt = ReturnPolicy.GetDeadlineUtc(outbound!.DeliveredAt!.Value),
                    CreatedAt = nowUtc
                };

                foreach (var line in normalizedLines)
                {
                    request.Items.Add(new ReturnItem
                    {
                        OrderItemId = line.OrderItemId,
                        RequestedQuantity = line.Quantity,
                        CreatedAt = nowUtc
                    });
                }

                foreach (var evidence in normalizedEvidence)
                {
                    request.Evidence.Add(new ReturnEvidence
                    {
                        Type = evidence.Type,
                        Url = evidence.Url,
                        Caption = evidence.Caption,
                        CreatedAt = nowUtc
                    });
                }

                order.ReturnRequests.Add(request);
                tracker.MoveTo(CommerceFlowStage.WriteTimeline);
                AddHistory(
                    order,
                    ReturnRequestStatus.Requested.ToString(),
                    "RETURN_REQUESTED",
                    "Khách hàng đã gửi yêu cầu hoàn trả",
                    $"Yêu cầu {code}: {reasonCode} · {reasonText}",
                    requestedBy,
                    nowUtc,
                    tracker.CorrelationId,
                    customerVisible: true);

                tracker.MoveTo(CommerceFlowStage.SaveChanges);
                await _context.SaveChangesAsync(token);
                tracker.AddMetadata("ReturnRequestId", request.Id);
                return request;
            },
            cancellationToken);
    }

    public Task<ReturnRequest> StartReviewAsync(
        StartReturnReviewCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tracker = BuildTracker(
            command.ReturnRequestId,
            "StartReview",
            command.Actor);

        var actor = NormalizeRequired(
            command.Actor,
            ActorMaxLength,
            "Người duyệt",
            "RETURN_REVIEW_ACTOR_REQUIRED",
            tracker);
        var note = NormalizeOptional(command.Note, NoteMaxLength);

        return CommerceFlowTransaction.ExecuteAsync(
            _context,
            tracker,
            async token =>
            {
                tracker.MoveTo(CommerceFlowStage.LoadAggregate);
                var request = await LoadReturnAggregateAsync(command.ReturnRequestId, token);

                tracker.MoveTo(
                    CommerceFlowStage.ValidateStateTransition,
                    request.Status.ToString());
                EnsureStatus(
                    request,
                    ReturnRequestStatus.Requested,
                    "RETURN_REVIEW_REQUIRES_REQUESTED",
                    tracker);
                ApplyOriginalRowVersion(request, command.RowVersion, tracker);

                var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
                request.Status = ReturnRequestStatus.UnderReview;
                request.ReviewedBy = actor;
                request.ReviewedAt = nowUtc;
                request.ReviewNote = note;
                request.UpdatedAt = nowUtc;

                AddHistory(
                    request.Order,
                    ReturnRequestStatus.UnderReview.ToString(),
                    "RETURN_UNDER_REVIEW",
                    "Yêu cầu hoàn trả đang được duyệt",
                    note,
                    actor,
                    nowUtc,
                    tracker.CorrelationId,
                    customerVisible: true);

                tracker.MoveTo(CommerceFlowStage.SaveChanges);
                await _context.SaveChangesAsync(token);
                return request;
            },
            cancellationToken);
    }

    public Task<ReturnRequest> DecideAsync(
        DecideReturnRequestCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tracker = BuildTracker(
            command.ReturnRequestId,
            command.Approve ? "ApproveReturnRequest" : "RejectReturnRequest",
            command.Actor);

        var actor = NormalizeRequired(
            command.Actor,
            ActorMaxLength,
            "Người duyệt",
            "RETURN_REVIEW_ACTOR_REQUIRED",
            tracker);
        var note = NormalizeRequired(
            command.Note,
            ReasonTextMaxLength,
            "Ghi chú duyệt",
            "RETURN_REVIEW_NOTE_REQUIRED",
            tracker);

        return CommerceFlowTransaction.ExecuteAsync(
            _context,
            tracker,
            async token =>
            {
                tracker.MoveTo(CommerceFlowStage.LoadAggregate);
                var request = await LoadReturnAggregateAsync(command.ReturnRequestId, token);

                tracker.MoveTo(
                    CommerceFlowStage.ValidateStateTransition,
                    request.Status.ToString());
                EnsureStatus(
                    request,
                    ReturnRequestStatus.UnderReview,
                    "RETURN_DECISION_REQUIRES_UNDER_REVIEW",
                    tracker);
                ApplyOriginalRowVersion(request, command.RowVersion, tracker);

                var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
                request.ReviewedBy = actor;
                request.ReviewedAt = nowUtc;
                request.ReviewNote = note;
                request.UpdatedAt = nowUtc;

                if (!command.Approve)
                {
                    foreach (var item in request.Items)
                    {
                        item.ApprovedQuantity = 0;
                    }

                    request.Status = ReturnRequestStatus.Rejected;
                    request.CompletedAt = nowUtc;

                    AddHistory(
                        request.Order,
                        ReturnRequestStatus.Rejected.ToString(),
                        "RETURN_REJECTED",
                        "Yêu cầu hoàn trả bị từ chối",
                        note,
                        actor,
                        nowUtc,
                        tracker.CorrelationId,
                        customerVisible: true);

                    tracker.MoveTo(CommerceFlowStage.SaveChanges);
                    await _context.SaveChangesAsync(token);
                    return request;
                }

                var approvalByItemId = NormalizeApprovalLines(command.Lines, tracker);
                if (approvalByItemId.Count != request.Items.Count)
                {
                    throw new BusinessRuleViolationException(
                        "RETURN_APPROVAL_LINES_INCOMPLETE",
                        "Phải nhập số lượng duyệt cho mọi dòng trong yêu cầu hoàn trả.",
                        tracker.Snapshot());
                }

                var availability = BuildAvailability(
                    request.Order,
                    excludeReturnRequestId: request.Id);
                var approvedTotal = 0;

                foreach (var item in request.Items)
                {
                    if (!approvalByItemId.TryGetValue(item.Id, out var approvedQuantity))
                    {
                        throw new BusinessRuleViolationException(
                            "RETURN_APPROVAL_ITEM_MISSING",
                            $"Thiếu quyết định cho return item {item.Id}.",
                            tracker.Snapshot());
                    }

                    if (approvedQuantity < 0
                        || approvedQuantity > item.RequestedQuantity)
                    {
                        throw new BusinessRuleViolationException(
                            "RETURN_APPROVED_QUANTITY_INVALID",
                            $"Số lượng duyệt của {item.OrderItem.Sku} phải từ 0 đến {item.RequestedQuantity}.",
                            tracker.Snapshot());
                    }

                    var available = availability[item.OrderItemId].AvailableQuantity;
                    if (approvedQuantity > available)
                    {
                        throw new BusinessRuleViolationException(
                            "RETURN_QUANTITY_RESERVED_BY_OTHER_REQUEST",
                            $"Sản phẩm {item.OrderItem.Sku} chỉ còn {available} đơn vị chưa bị yêu cầu hoàn trả khác giữ chỗ.",
                            tracker.Snapshot());
                    }

                    item.ApprovedQuantity = approvedQuantity;
                    approvedTotal += approvedQuantity;
                }

                if (approvedTotal <= 0)
                {
                    throw new BusinessRuleViolationException(
                        "RETURN_APPROVAL_REQUIRES_QUANTITY",
                        "Phê duyệt phải có ít nhất một sản phẩm.",
                        tracker.Snapshot());
                }

                request.Status = ReturnRequestStatus.Approved;
                request.ApprovedAt = nowUtc;
                request.CompletedAt = null;

                AddHistory(
                    request.Order,
                    ReturnRequestStatus.Approved.ToString(),
                    "RETURN_APPROVED",
                    "Yêu cầu hoàn trả đã được phê duyệt",
                    $"Đã duyệt {approvedTotal} sản phẩm. {note}",
                    actor,
                    nowUtc,
                    tracker.CorrelationId,
                    customerVisible: true);

                tracker.MoveTo(CommerceFlowStage.SaveChanges);
                await _context.SaveChangesAsync(token);
                return request;
            },
            cancellationToken);
    }

    public Task<ReturnRequest> BeginInspectionAsync(
        BeginReturnInspectionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tracker = BuildTracker(
            command.ReturnRequestId,
            "BeginReturnInspection",
            command.Actor);
        var actor = NormalizeRequired(
            command.Actor,
            ActorMaxLength,
            "Nhân viên kho",
            "RETURN_RECEIPT_ACTOR_REQUIRED",
            tracker);
        var note = NormalizeOptional(command.Note, NoteMaxLength);
        var receiptByItemId = NormalizeReceiptLines(command.Lines, tracker);

        return CommerceFlowTransaction.ExecuteAsync(
            _context,
            tracker,
            async token =>
            {
                tracker.MoveTo(CommerceFlowStage.LoadAggregate);
                var request = await LoadReturnAggregateAsync(command.ReturnRequestId, token);

                tracker.MoveTo(
                    CommerceFlowStage.ValidateStateTransition,
                    request.Status.ToString());
                EnsureStatus(
                    request,
                    ReturnRequestStatus.ReceivedAtWarehouse,
                    "RETURN_INSPECTION_REQUIRES_WAREHOUSE_RECEIPT",
                    tracker);
                ApplyOriginalRowVersion(request, command.RowVersion, tracker);

                var deliveredReturnShipment = request.Shipments
                    .Where(item =>
                        item.Direction == ShipmentDirection.Return
                        && item.Status == ShipmentStatus.Delivered
                        && item.DeliveredAt.HasValue)
                    .OrderByDescending(item => item.DeliveredAt)
                    .FirstOrDefault();

                if (deliveredReturnShipment is null)
                {
                    throw new BusinessRuleViolationException(
                        "RETURN_SHIPMENT_NOT_DELIVERED_TO_WAREHOUSE",
                        "Chỉ được ghi nhận hàng trả khi GHN đã giao vận đơn chiều về tới kho.",
                        tracker.Snapshot());
                }

                var approvedItems = request.Items
                    .Where(item => item.ApprovedQuantity > 0)
                    .OrderBy(item => item.Id)
                    .ToArray();

                if (receiptByItemId.Count != approvedItems.Length)
                {
                    throw new BusinessRuleViolationException(
                        "RETURN_RECEIPT_LINES_INCOMPLETE",
                        "Phải ghi nhận số lượng nhận cho mọi dòng đã được phê duyệt.",
                        tracker.Snapshot());
                }

                var inventoryLines = new List<ReturnInventoryReceiptLine>();
                foreach (var item in approvedItems)
                {
                    if (!receiptByItemId.TryGetValue(item.Id, out var receivedQuantity))
                    {
                        throw new BusinessRuleViolationException(
                            "RETURN_RECEIPT_ITEM_MISSING",
                            $"Thiếu số lượng nhận cho return item {item.Id}.",
                            tracker.Snapshot());
                    }

                    if (receivedQuantity < 0
                        || receivedQuantity > item.ApprovedQuantity)
                    {
                        throw new BusinessRuleViolationException(
                            "RETURN_RECEIVED_QUANTITY_INVALID",
                            $"Số lượng nhận của {item.OrderItem.Sku} phải từ 0 đến {item.ApprovedQuantity}.",
                            tracker.Snapshot());
                    }

                    item.ReceivedQuantity = receivedQuantity;
                    if (receivedQuantity > 0)
                    {
                        inventoryLines.Add(new ReturnInventoryReceiptLine(
                            item.Id,
                            item.OrderItemId,
                            item.OrderItem.ProductVariantId,
                            receivedQuantity));
                    }
                }

                if (inventoryLines.Count == 0)
                {
                    throw new BusinessRuleViolationException(
                        "RETURN_RECEIPT_REQUIRES_QUANTITY",
                        "Kho phải nhận ít nhất một sản phẩm để bắt đầu kiểm định.",
                        tracker.Snapshot());
                }

                tracker.MoveTo(CommerceFlowStage.WriteInventoryLedger);
                await _inventoryService.RecordReturnReceiptAsync(
                    request.Id,
                    inventoryLines,
                    string.IsNullOrWhiteSpace(note)
                        ? $"Kho tiếp nhận hàng trả cho {request.Code}."
                        : note,
                    $"return:{request.Id}:receipt",
                    actor,
                    token);

                var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
                request.ReceivedAt ??= deliveredReturnShipment.DeliveredAt ?? nowUtc;
                request.Status = ReturnRequestStatus.Inspecting;
                request.UpdatedAt = nowUtc;

                tracker.MoveTo(CommerceFlowStage.WriteTimeline);
                AddHistory(
                    request.Order,
                    ReturnRequestStatus.Inspecting.ToString(),
                    "RETURN_INSPECTION_STARTED",
                    "Kho đã tiếp nhận và bắt đầu kiểm định",
                    $"Đã nhận {inventoryLines.Sum(item => item.Quantity)} sản phẩm. {note}",
                    actor,
                    nowUtc,
                    tracker.CorrelationId,
                    customerVisible: true);

                tracker.MoveTo(CommerceFlowStage.SaveChanges);
                await _context.SaveChangesAsync(token);
                return request;
            },
            cancellationToken);
    }

    public Task<ReturnRequest> CompleteInspectionAsync(
        CompleteReturnInspectionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tracker = new CommerceFlowTracker(
            "ReturnInspection",
            nameof(ReturnRequest),
            command.ReturnRequestId.ToString(CultureInfo.InvariantCulture),
            "CompleteInspection",
            idempotencyKey: command.IdempotencyKey);

        var actor = NormalizeRequired(
            command.Actor,
            ActorMaxLength,
            "Kiểm định viên",
            "RETURN_INSPECTOR_REQUIRED",
            tracker);
        var note = NormalizeRequired(
            command.Note,
            NoteMaxLength,
            "Kết luận kiểm định",
            "RETURN_INSPECTION_NOTE_REQUIRED",
            tracker);
        var idempotencyKey = NormalizeRequired(
            command.IdempotencyKey,
            IdempotencyKeyMaxLength,
            "Idempotency key",
            "RETURN_INSPECTION_IDEMPOTENCY_REQUIRED",
            tracker);
        var lineByItemId = NormalizeInspectionLines(command.Lines, tracker);
        var payloadHash = BuildInspectionPayloadHash(
            note,
            lineByItemId.Values);

        return CommerceFlowTransaction.ExecuteAsync(
            _context,
            tracker,
            async token =>
            {
                tracker.MoveTo(CommerceFlowStage.LoadAggregate);
                var request = await LoadReturnAggregateAsync(command.ReturnRequestId, token);

                tracker.MoveTo(CommerceFlowStage.Deduplicate);
                var existingInspection = request.Inspections
                    .SingleOrDefault(item => item.IdempotencyKey == idempotencyKey);
                if (existingInspection is not null)
                {
                    if (!string.Equals(
                            existingInspection.PayloadHash,
                            payloadHash,
                            StringComparison.Ordinal))
                    {
                        throw new IdempotencyConflictException(
                            "RETURN_INSPECTION_IDEMPOTENCY_PAYLOAD_CONFLICT",
                            "Idempotency key kiểm định đã được dùng cho payload khác.",
                            tracker.Snapshot());
                    }

                    if (request.Status is not ReturnRequestStatus.RefundPending
                        and not ReturnRequestStatus.RejectedAfterInspection
                        and not ReturnRequestStatus.Refunded
                        and not ReturnRequestStatus.Closed)
                    {
                        throw new IdempotencyConflictException(
                            "RETURN_INSPECTION_IDEMPOTENCY_STATE_MISMATCH",
                            "Inspection đã tồn tại nhưng return request chưa ở trạng thái sau kiểm định.",
                            tracker.Snapshot());
                    }

                    return request;
                }

                tracker.MoveTo(
                    CommerceFlowStage.ValidateStateTransition,
                    request.Status.ToString());
                EnsureStatus(
                    request,
                    ReturnRequestStatus.Inspecting,
                    "RETURN_INSPECTION_REQUIRES_INSPECTING",
                    tracker);
                ApplyOriginalRowVersion(request, command.RowVersion, tracker);

                var receivedItems = request.Items
                    .Where(item => item.ReceivedQuantity > 0)
                    .OrderBy(item => item.Id)
                    .ToArray();
                if (lineByItemId.Count != receivedItems.Length)
                {
                    throw new BusinessRuleViolationException(
                        "RETURN_INSPECTION_LINES_INCOMPLETE",
                        "Phải kiểm định mọi dòng sản phẩm đã được kho tiếp nhận.",
                        tracker.Snapshot());
                }

                var inventoryLines = new List<ReturnInventoryDispositionLine>();
                var acceptedTotal = 0;
                var rejectedTotal = 0;

                foreach (var item in receivedItems)
                {
                    if (!lineByItemId.TryGetValue(item.Id, out var line))
                    {
                        throw new BusinessRuleViolationException(
                            "RETURN_INSPECTION_ITEM_MISSING",
                            $"Thiếu kết quả cho return item {item.Id}.",
                            tracker.Snapshot());
                    }

                    if (line.AcceptedQuantity < 0
                        || line.RejectedQuantity < 0
                        || line.AcceptedQuantity + line.RejectedQuantity != item.ReceivedQuantity)
                    {
                        throw new BusinessRuleViolationException(
                            "RETURN_INSPECTION_QUANTITY_UNBALANCED",
                            $"Accepted + Rejected của {item.OrderItem.Sku} phải bằng {item.ReceivedQuantity}.",
                            tracker.Snapshot());
                    }

                    if (line.RestockQuantity < 0
                        || line.WriteOffQuantity < 0
                        || line.RestockQuantity + line.WriteOffQuantity != line.AcceptedQuantity)
                    {
                        throw new BusinessRuleViolationException(
                            "RETURN_INSPECTION_DISPOSITION_UNBALANCED",
                            $"Restock + WriteOff của {item.OrderItem.Sku} phải bằng AcceptedQuantity.",
                            tracker.Snapshot());
                    }

                    if (line.ConditionCode == ReturnItemCondition.Restockable
                        && (line.RestockQuantity != line.AcceptedQuantity
                            || line.WriteOffQuantity != 0))
                    {
                        throw new BusinessRuleViolationException(
                            "RETURN_RESTOCKABLE_REQUIRES_FULL_RESTOCK",
                            $"Dòng {item.OrderItem.Sku} được đánh dấu đủ điều kiện bán lại nên toàn bộ số lượng chấp nhận phải được nhập kho.",
                            tracker.Snapshot());
                    }

                    if (line.ConditionCode == ReturnItemCondition.Mixed
                        && (line.RestockQuantity <= 0 || line.WriteOffQuantity <= 0))
                    {
                        throw new BusinessRuleViolationException(
                            "RETURN_MIXED_CONDITION_REQUIRES_SPLIT_DISPOSITION",
                            $"Dòng {item.OrderItem.Sku} chỉ được chọn Mixed khi có cả số lượng nhập lại kho và số lượng loại bỏ.",
                            tracker.Snapshot());
                    }

                    if (line.ConditionCode is not ReturnItemCondition.Restockable
                        and not ReturnItemCondition.Mixed
                        && (line.RestockQuantity != 0
                            || line.WriteOffQuantity != line.AcceptedQuantity))
                    {
                        throw new BusinessRuleViolationException(
                            "RETURN_NON_RESTOCKABLE_REQUIRES_FULL_WRITE_OFF",
                            $"Dòng {item.OrderItem.Sku} có tình trạng {line.ConditionCode} nên toàn bộ số lượng chấp nhận phải được loại khỏi tồn bán.",
                            tracker.Snapshot());
                    }

                    item.AcceptedQuantity = line.AcceptedQuantity;
                    item.RejectedQuantity = line.RejectedQuantity;
                    item.RestockQuantity = line.RestockQuantity;
                    item.WriteOffQuantity = line.WriteOffQuantity;
                    item.ConditionCode = line.ConditionCode;
                    item.InspectionNote = NormalizeOptional(line.Note, ReasonTextMaxLength);
                    item.RefundAmount = CalculateRefundAmount(
                        item.OrderItem,
                        line.AcceptedQuantity);

                    acceptedTotal += line.AcceptedQuantity;
                    rejectedTotal += line.RejectedQuantity;

                    inventoryLines.Add(new ReturnInventoryDispositionLine(
                        item.Id,
                        item.OrderItemId,
                        item.OrderItem.ProductVariantId,
                        line.RestockQuantity,
                        line.WriteOffQuantity));
                }

                tracker.MoveTo(CommerceFlowStage.WriteInventoryLedger);
                await _inventoryService.ApplyReturnInspectionAsync(
                    request.Id,
                    inventoryLines,
                    note,
                    $"return:{request.Id}:inspection",
                    actor,
                    token);

                var result = acceptedTotal switch
                {
                    0 => ReturnInspectionResult.Rejected,
                    _ when rejectedTotal == 0 => ReturnInspectionResult.Accepted,
                    _ => ReturnInspectionResult.PartiallyAccepted
                };
                var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

                request.Inspections.Add(new ReturnInspection
                {
                    Inspector = actor,
                    Result = result,
                    IdempotencyKey = idempotencyKey,
                    PayloadHash = payloadHash,
                    Note = note,
                    CreatedAt = nowUtc
                });
                request.InspectionResult = result;
                request.InspectedAt = nowUtc;
                request.UpdatedAt = nowUtc;

                tracker.MoveTo(CommerceFlowStage.ApplyStateTransition);
                if (acceptedTotal == 0)
                {
                    request.Status = ReturnRequestStatus.RejectedAfterInspection;
                    request.CompletedAt = nowUtc;

                    AddHistory(
                        request.Order,
                        ReturnRequestStatus.RejectedAfterInspection.ToString(),
                        "RETURN_REJECTED_AFTER_INSPECTION",
                        "Hàng trả không đạt điều kiện hoàn tiền",
                        note,
                        actor,
                        nowUtc,
                        tracker.CorrelationId,
                        customerVisible: true);
                }
                else
                {
                    request.Status = ReturnRequestStatus.RefundPending;
                    var refundAmount = request.Items.Sum(item => item.RefundAmount);

                    AddHistory(
                        request.Order,
                        ReturnRequestStatus.RefundPending.ToString(),
                        "RETURN_REFUND_PENDING",
                        "Đã kiểm định, đang chờ hoàn tiền",
                        $"Kết quả {result}; accepted {acceptedTotal}, rejected {rejectedTotal}; số tiền dự kiến {refundAmount:N0} ₫. {note}",
                        actor,
                        nowUtc,
                        tracker.CorrelationId,
                        customerVisible: true);
                }

                tracker.MoveTo(CommerceFlowStage.SaveChanges);
                await _context.SaveChangesAsync(token);
                return request;
            },
            cancellationToken);
    }

    private async Task<Order?> LoadOrderForEligibilityAsync(
        Guid publicToken,
        bool tracked,
        CancellationToken cancellationToken)
    {
        IQueryable<Order> query = _context.Orders;
        if (!tracked)
        {
            query = query.AsNoTracking();
        }

        return await query
            .Include(item => item.Items)
            .Include(item => item.Shipments)
            .Include(item => item.CancellationRequests)
                .ThenInclude(item => item.Items)
            .Include(item => item.ReturnRequests)
                .ThenInclude(item => item.Items)
            .Include(item => item.StatusHistory)
            .SingleOrDefaultAsync(item => item.PublicToken == publicToken, cancellationToken);
    }

    private async Task<ReturnRequest> LoadReturnAggregateAsync(
        long returnRequestId,
        CancellationToken cancellationToken)
    {
        if (returnRequestId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(returnRequestId));
        }

        return await _context.ReturnRequests
            .Include(item => item.Items)
                .ThenInclude(item => item.OrderItem)
            .Include(item => item.Inspections)
            .Include(item => item.Evidence)
            .Include(item => item.Shipments)
            .Include(item => item.Order)
                .ThenInclude(order => order.Items)
            .Include(item => item.Order)
                .ThenInclude(order => order.Shipments)
            .Include(item => item.Order)
                .ThenInclude(order => order.CancellationRequests)
                    .ThenInclude(cancellation => cancellation.Items)
            .Include(item => item.Order)
                .ThenInclude(order => order.ReturnRequests)
                    .ThenInclude(returnRequest => returnRequest.Items)
            .Include(item => item.Order)
                .ThenInclude(order => order.StatusHistory)
            .SingleOrDefaultAsync(item => item.Id == returnRequestId, cancellationToken)
            ?? throw new BusinessRuleViolationException(
                "RETURN_REQUEST_NOT_FOUND",
                $"Không tìm thấy yêu cầu hoàn trả {returnRequestId}.",
                new CommerceFlowContext(
                    "ReturnRequest",
                    CommerceFlowStage.LoadAggregate,
                    nameof(ReturnRequest),
                    returnRequestId.ToString(CultureInfo.InvariantCulture),
                    null,
                    "LoadReturnAggregate",
                    Guid.NewGuid().ToString("N"),
                    null,
                    new Dictionary<string, string>()));
    }

    private static (string ErrorCode, string Message)? GetEligibilityError(
        Order order,
        Shipment? outbound,
        DateTime nowUtc)
    {
        if (order.OrderStatus != OrderStatus.Completed
            || order.FulfillmentStatus != FulfillmentStatus.Delivered)
        {
            return (
                "RETURN_ORDER_NOT_COMPLETED",
                $"Chỉ đơn đã hoàn tất và giao thành công mới được yêu cầu hoàn trả. Hiện tại: {order.OrderStatus}/{order.FulfillmentStatus}.");
        }

        if (outbound?.DeliveredAt is not DateTime deliveredAt)
        {
            return (
                "RETURN_NOT_DELIVERED",
                "Không tìm thấy vận đơn chiều đi đã được GHN xác nhận Delivered.");
        }

        var deadline = ReturnPolicy.GetDeadlineUtc(deliveredAt);
        if (ReturnPolicy.NormalizeUtc(nowUtc) > deadline)
        {
            return (
                "RETURN_WINDOW_EXPIRED",
                $"Thời hạn hoàn trả đã kết thúc lúc {deadline:O}. GHN xác nhận giao lúc {deliveredAt:O}.");
        }

        if (order.CancellationRequests.Any(item =>
                item.Status == OrderCancellationStatus.Pending))
        {
            return (
                "RETURN_BLOCKED_BY_PENDING_CANCELLATION",
                "Đơn đang có yêu cầu hủy chưa xử lý; không thể đồng thời mở return workflow.");
        }

        return null;
    }

    private static Dictionary<int, AvailabilityState> BuildAvailability(
        Order order,
        long? excludeReturnRequestId)
    {
        var cancelledByOrderItem = order.CancellationRequests
            .Where(item => item.Status == OrderCancellationStatus.Approved)
            .SelectMany(item => item.Items)
            .GroupBy(item => item.OrderItemId)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(item => item.ApprovedQuantity));

        var reservedByOrderItem = order.ReturnRequests
            .Where(item =>
                item.Id != excludeReturnRequestId
                && ReturnPolicy.ReservesQuantity(item.Status))
            .SelectMany(item => item.Items)
            .GroupBy(item => item.OrderItemId)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(item =>
                    item.ApprovedQuantity > 0
                        ? item.ApprovedQuantity
                        : item.RequestedQuantity));

        return order.Items.ToDictionary(
            item => item.Id,
            item =>
            {
                var cancelled = cancelledByOrderItem.GetValueOrDefault(item.Id);
                var reserved = reservedByOrderItem.GetValueOrDefault(item.Id);
                return new AvailabilityState(
                    cancelled,
                    reserved,
                    Math.Max(0, item.Quantity - cancelled - reserved));
            });
    }

    private void ApplyOriginalRowVersion(
        ReturnRequest request,
        byte[] rowVersion,
        CommerceFlowTracker tracker)
    {
        tracker.MoveTo(CommerceFlowStage.ValidateConcurrency);

        if (rowVersion is not { Length: > 0 })
        {
            throw new BusinessRuleViolationException(
                "RETURN_ROW_VERSION_REQUIRED",
                "RowVersion của yêu cầu hoàn trả là bắt buộc.",
                tracker.Snapshot());
        }

        _context.Entry(request)
            .Property(item => item.RowVersion)
            .OriginalValue = rowVersion;
    }

    private static void EnsureStatus(
        ReturnRequest request,
        ReturnRequestStatus required,
        string errorCode,
        CommerceFlowTracker tracker)
    {
        if (request.Status != required)
        {
            throw new InvalidStateTransitionException(
                errorCode,
                $"Return request phải ở {required}; trạng thái hiện tại là {request.Status}.",
                tracker.Snapshot());
        }
    }

    private static IReadOnlyList<ReturnRequestLineCommand> NormalizeRequestLines(
        IReadOnlyCollection<ReturnRequestLineCommand> lines,
        CommerceFlowTracker tracker)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new BusinessRuleViolationException(
                "RETURN_LINES_REQUIRED",
                "Yêu cầu hoàn trả phải có ít nhất một sản phẩm.",
                tracker.Snapshot());
        }

        if (lines.Any(item => item.OrderItemId <= 0 || item.Quantity <= 0))
        {
            throw new BusinessRuleViolationException(
                "RETURN_LINE_INVALID",
                "Dòng hoàn trả chứa mã hoặc số lượng không hợp lệ.",
                tracker.Snapshot());
        }

        var duplicate = lines
            .GroupBy(item => item.OrderItemId)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new BusinessRuleViolationException(
                "RETURN_LINE_DUPLICATED",
                $"Order item {duplicate.Key} xuất hiện nhiều lần.",
                tracker.Snapshot());
        }

        return lines.OrderBy(item => item.OrderItemId).ToArray();
    }

    private static Dictionary<long, int> NormalizeApprovalLines(
        IReadOnlyCollection<ReturnApprovalLineCommand> lines,
        CommerceFlowTracker tracker)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new BusinessRuleViolationException(
                "RETURN_APPROVAL_LINES_REQUIRED",
                "Danh sách duyệt không được để trống.",
                tracker.Snapshot());
        }

        if (lines.Any(item => item.ReturnItemId <= 0 || item.ApprovedQuantity < 0))
        {
            throw new BusinessRuleViolationException(
                "RETURN_APPROVAL_LINE_INVALID",
                "Dòng duyệt chứa mã hoặc số lượng không hợp lệ.",
                tracker.Snapshot());
        }

        var duplicate = lines
            .GroupBy(item => item.ReturnItemId)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new BusinessRuleViolationException(
                "RETURN_APPROVAL_LINE_DUPLICATED",
                $"Return item {duplicate.Key} xuất hiện nhiều lần.",
                tracker.Snapshot());
        }

        return lines.ToDictionary(item => item.ReturnItemId, item => item.ApprovedQuantity);
    }

    private static Dictionary<long, int> NormalizeReceiptLines(
        IReadOnlyCollection<ReturnReceiptLineCommand> lines,
        CommerceFlowTracker tracker)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new BusinessRuleViolationException(
                "RETURN_RECEIPT_LINES_REQUIRED",
                "Danh sách hàng kho nhận không được để trống.",
                tracker.Snapshot());
        }

        if (lines.Any(item => item.ReturnItemId <= 0 || item.ReceivedQuantity < 0))
        {
            throw new BusinessRuleViolationException(
                "RETURN_RECEIPT_LINE_INVALID",
                "Dòng nhận hàng chứa mã hoặc số lượng không hợp lệ.",
                tracker.Snapshot());
        }

        var duplicate = lines
            .GroupBy(item => item.ReturnItemId)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new BusinessRuleViolationException(
                "RETURN_RECEIPT_LINE_DUPLICATED",
                $"Return item {duplicate.Key} xuất hiện nhiều lần.",
                tracker.Snapshot());
        }

        return lines.ToDictionary(item => item.ReturnItemId, item => item.ReceivedQuantity);
    }

    private static Dictionary<long, ReturnInspectionLineCommand> NormalizeInspectionLines(
        IReadOnlyCollection<ReturnInspectionLineCommand> lines,
        CommerceFlowTracker tracker)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new BusinessRuleViolationException(
                "RETURN_INSPECTION_LINES_REQUIRED",
                "Danh sách kiểm định không được để trống.",
                tracker.Snapshot());
        }

        if (lines.Any(item =>
                item.ReturnItemId <= 0
                || item.AcceptedQuantity < 0
                || item.RejectedQuantity < 0
                || item.RestockQuantity < 0
                || item.WriteOffQuantity < 0))
        {
            throw new BusinessRuleViolationException(
                "RETURN_INSPECTION_LINE_INVALID",
                "Dòng kiểm định chứa mã hoặc số lượng không hợp lệ.",
                tracker.Snapshot());
        }

        var duplicate = lines
            .GroupBy(item => item.ReturnItemId)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new BusinessRuleViolationException(
                "RETURN_INSPECTION_LINE_DUPLICATED",
                $"Return item {duplicate.Key} xuất hiện nhiều lần.",
                tracker.Snapshot());
        }

        return lines.ToDictionary(item => item.ReturnItemId);
    }

    private static IReadOnlyList<ReturnEvidenceCommand> NormalizeEvidence(
        IReadOnlyCollection<ReturnEvidenceCommand> evidence,
        CommerceFlowTracker tracker)
    {
        if (evidence is null || evidence.Count == 0)
        {
            return [];
        }

        if (evidence.Count > MaximumEvidenceCount)
        {
            throw new BusinessRuleViolationException(
                "RETURN_EVIDENCE_LIMIT_EXCEEDED",
                $"Tối đa {MaximumEvidenceCount} hình ảnh hoặc video cho một yêu cầu.",
                tracker.Snapshot());
        }

        var normalized = new List<ReturnEvidenceCommand>();
        foreach (var item in evidence)
        {
            if (!Uri.TryCreate(item.Url?.Trim(), UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https"))
            {
                throw new BusinessRuleViolationException(
                    "RETURN_EVIDENCE_URL_INVALID",
                    "Evidence URL phải là địa chỉ HTTP/HTTPS tuyệt đối.",
                    tracker.Snapshot());
            }

            var url = uri.ToString();
            if (url.Length > EvidenceUrlMaxLength)
            {
                throw new BusinessRuleViolationException(
                    "RETURN_EVIDENCE_URL_TOO_LONG",
                    $"Evidence URL không được vượt {EvidenceUrlMaxLength} ký tự.",
                    tracker.Snapshot());
            }

            var caption = NormalizeOptional(
                item.Caption,
                EvidenceCaptionMaxLength);
            normalized.Add(new ReturnEvidenceCommand(
                item.Type,
                url,
                caption));
        }

        var duplicate = normalized
            .GroupBy(item => item.Url, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new BusinessRuleViolationException(
                "RETURN_EVIDENCE_DUPLICATED",
                $"Evidence URL bị lặp: {duplicate.Key}.",
                tracker.Snapshot());
        }

        return normalized
            .OrderBy(item => item.Url, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void EnsureSameRequestPayload(
        ReturnRequest existing,
        Guid orderPublicToken,
        string reasonCode,
        string reasonText,
        IReadOnlyCollection<ReturnRequestLineCommand> lines,
        IReadOnlyCollection<ReturnEvidenceCommand> evidence,
        CommerceFlowTracker tracker)
    {
        var existingLines = existing.Items
            .OrderBy(item => item.OrderItemId)
            .Select(item => new ReturnRequestLineCommand(
                item.OrderItemId,
                item.RequestedQuantity))
            .ToArray();

        var existingEvidence = existing.Evidence
            .OrderBy(item => item.Url, StringComparer.OrdinalIgnoreCase)
            .Select(item => new ReturnEvidenceCommand(
                item.Type,
                item.Url,
                item.Caption))
            .ToArray();

        if (existing.Order.PublicToken != orderPublicToken
            || !string.Equals(existing.ReasonCode, reasonCode, StringComparison.Ordinal)
            || !string.Equals(existing.ReasonText, reasonText, StringComparison.Ordinal)
            || !existingLines.SequenceEqual(lines)
            || !existingEvidence.SequenceEqual(evidence))
        {
            throw new IdempotencyConflictException(
                "RETURN_IDEMPOTENCY_PAYLOAD_CONFLICT",
                "Idempotency key đã được dùng cho một yêu cầu hoàn trả có payload khác.",
                tracker.Snapshot());
        }
    }

    private static string BuildInspectionPayloadHash(
        string note,
        IEnumerable<ReturnInspectionLineCommand> lines)
    {
        var canonical = new StringBuilder(note.Trim());
        foreach (var line in lines.OrderBy(item => item.ReturnItemId))
        {
            canonical.Append('|')
                .Append(line.ReturnItemId)
                .Append(':').Append(line.AcceptedQuantity)
                .Append(':').Append(line.RejectedQuantity)
                .Append(':').Append(line.RestockQuantity)
                .Append(':').Append(line.WriteOffQuantity)
                .Append(':').Append(line.ConditionCode)
                .Append(':').Append(line.Note?.Trim());
        }

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }

    private static decimal CalculateRefundAmount(
        OrderItem orderItem,
        int acceptedQuantity)
    {
        if (acceptedQuantity <= 0)
        {
            return 0m;
        }

        var perUnit = orderItem.Quantity > 0
            ? orderItem.LineTotal / orderItem.Quantity
            : orderItem.UnitPrice;
        return Math.Round(
            perUnit * acceptedQuantity,
            2,
            MidpointRounding.AwayFromZero);
    }

    private static CommerceFlowTracker BuildTracker(
        long returnRequestId,
        string action,
        string actor) =>
        new CommerceFlowTracker(
            "ReturnRequest",
            nameof(ReturnRequest),
            returnRequestId.ToString(CultureInfo.InvariantCulture),
            action)
            .AddMetadata("Actor", actor);

    private static void AddHistory(
        Order order,
        string toStatus,
        string code,
        string title,
        string? description,
        string actor,
        DateTime occurredAt,
        string correlationId,
        bool customerVisible)
    {
        order.StatusHistory.Add(new OrderStatusHistory
        {
            Category = OrderHistoryCategory.Return,
            FromStatus = null,
            ToStatus = toStatus,
            Code = code,
            Title = title,
            Description = description,
            ChangedBy = string.IsNullOrWhiteSpace(actor) ? "System" : actor.Trim(),
            CustomerVisible = customerVisible,
            OccurredAt = occurredAt,
            CorrelationId = ToCorrelationId(correlationId)
        });
    }

    private static string NormalizeRequired(
        string value,
        int maxLength,
        string label,
        string errorCode,
        CommerceFlowTracker tracker)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new BusinessRuleViolationException(
                errorCode,
                $"{label} là bắt buộc.",
                tracker.Snapshot());
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new BusinessRuleViolationException(
                $"{errorCode}_TOO_LONG",
                $"{label} không được vượt {maxLength} ký tự.",
                tracker.Snapshot());
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }

    private static string ToCorrelationId(string value) =>
        value.Length <= 64 ? value : value[..64];

    private sealed record AvailabilityState(
        int CancelledQuantity,
        int ReservedReturnQuantity,
        int AvailableQuantity);
}
