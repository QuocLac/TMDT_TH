using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Commerce.Inventory;
using WebApplication2.Services.Commerce.Flows;

namespace WebApplication2.Services.Commerce.Cancellations;

public sealed class OrderCancellationService : IOrderCancellationService
{
    private const int ActorMaxLength = 100;
    private const int ReasonCodeMaxLength = 50;
    private const int ReasonTextMaxLength = 500;
    private const int ReviewNoteMaxLength = 500;
    private const int IdempotencyKeyMaxLength = 128;

    private static readonly ShipmentStatus[] HandoverShipmentStatuses =
    [
        ShipmentStatus.Picking,
        ShipmentStatus.InTransit,
        ShipmentStatus.Delivered,
        ShipmentStatus.DeliveryFailed,
        ShipmentStatus.Returning,
        ShipmentStatus.Returned
    ];

    private readonly ApplicationDbContext _context;
    private readonly ICancellationInventoryService _inventoryService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OrderCancellationService> _logger;

    public OrderCancellationService(
        ApplicationDbContext context,
        ICancellationInventoryService inventoryService,
        TimeProvider timeProvider,
        ILogger<OrderCancellationService> logger)
    {
        _context = context;
        _inventoryService = inventoryService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<OrderCancellationSummary> GetSummaryAsync(
        int orderId,
        CancellationToken cancellationToken)
    {
        if (orderId <= 0)
        {
            throw new OrderCancellationException("Mã đơn hàng không hợp lệ.");
        }

        var order = await _context.Orders
            .AsNoTracking()
            .AsSplitQuery()
            .Include(item => item.Items)
            .Include(item => item.Shipments)
            .SingleOrDefaultAsync(item => item.Id == orderId, cancellationToken)
            ?? throw new OrderCancellationException($"Không tìm thấy đơn hàng {orderId}.");

        var requests = await _context.Set<OrderCancellationRequest>()
            .AsNoTracking()
            .AsSplitQuery()
            .Include(item => item.Items)
                .ThenInclude(item => item.OrderItem)
            .Where(item => item.OrderId == orderId)
            .OrderByDescending(item => item.RequestedAt)
            .ThenByDescending(item => item.Id)
            .ToArrayAsync(cancellationToken);

        var approvedByItem = requests
            .Where(item => item.Status == OrderCancellationStatus.Approved)
            .SelectMany(item => item.Items)
            .GroupBy(item => item.OrderItemId)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(item => item.ApprovedQuantity));

        var pendingByItem = requests
            .Where(item => item.Status == OrderCancellationStatus.Pending)
            .SelectMany(item => item.Items)
            .GroupBy(item => item.OrderItemId)
            .ToDictionary(
                group => group.Key,
                group => group.Sum(item => item.RequestedQuantity));

        var ineligibility = GetRequestIneligibilityMessage(order);
        var itemSummaries = order.Items
            .OrderBy(item => item.Id)
            .Select(item =>
            {
                var approved = approvedByItem.GetValueOrDefault(item.Id);
                var pending = pendingByItem.GetValueOrDefault(item.Id);
                return new OrderCancellationLineAvailability(
                    item.Id,
                    item.ProductName,
                    item.Sku,
                    item.Quantity,
                    approved,
                    pending,
                    Math.Max(0, item.Quantity - approved - pending));
            })
            .ToArray();

        return new OrderCancellationSummary(
            order.Id,
            order.Code,
            Convert.ToBase64String(order.RowVersion),
            ineligibility is null && itemSummaries.Any(item => item.CancellableQuantity > 0),
            ineligibility,
            itemSummaries,
            requests.Select(ToRequestSummary).ToArray());
    }

    public async Task<OrderCancellationSummary> RequestAsync(
        CreateOrderCancellationCommand command,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeCreateCommand(command);
        var tracker = new CommerceFlowTracker(
            "OrderCancellationRequest",
            nameof(Order),
            normalized.OrderId.ToString(CultureInfo.InvariantCulture),
            "RequestCancellation",
            correlationId: normalized.IdempotencyKey,
            idempotencyKey: normalized.IdempotencyKey);
        tracker.MoveTo(CommerceFlowStage.Deduplicate);

        var existing = await _context.Set<OrderCancellationRequest>()
            .AsNoTracking()
            .Include(item => item.Items)
            .SingleOrDefaultAsync(
                item => item.IdempotencyKey == normalized.IdempotencyKey,
                cancellationToken);

        if (existing is not null)
        {
            try
            {
                ValidateIdempotentRequest(existing, normalized);
            }
            catch (OrderCancellationException exception)
            {
                throw new OrderCancellationException(
                    exception.ErrorCode,
                    exception.DetailMessage,
                    tracker.Snapshot(),
                    exception);
            }

            tracker.MoveTo(CommerceFlowStage.Complete);
            return await GetSummaryAsync(normalized.OrderId, cancellationToken);
        }

        IDbContextTransaction? transaction = null;
        try
        {
            tracker.MoveTo(CommerceFlowStage.BeginTransaction);
            transaction = await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            tracker.MoveTo(CommerceFlowStage.Deduplicate);
            existing = await _context.Set<OrderCancellationRequest>()
                .AsNoTracking()
                .Include(item => item.Items)
                .SingleOrDefaultAsync(
                    item => item.IdempotencyKey == normalized.IdempotencyKey,
                    cancellationToken);

            if (existing is not null)
            {
                ValidateIdempotentRequest(existing, normalized);
                tracker.MoveTo(CommerceFlowStage.Commit);
                await transaction.CommitAsync(cancellationToken);
                tracker.MoveTo(CommerceFlowStage.Complete);
                return await GetSummaryAsync(normalized.OrderId, cancellationToken);
            }

            tracker.MoveTo(CommerceFlowStage.LoadAggregate);
            var order = await _context.Orders
                .Include(item => item.Items)
                .Include(item => item.Shipments)
                .SingleOrDefaultAsync(
                    item => item.Id == normalized.OrderId,
                    cancellationToken)
                ?? throw new OrderCancellationException(
                    $"Không tìm thấy đơn hàng {normalized.OrderId}.");

            tracker.MoveTo(CommerceFlowStage.ValidateConcurrency);
            EnsureRowVersion(
                order.RowVersion,
                normalized.OrderRowVersion,
                "Đơn hàng đã được cập nhật. Vui lòng tải lại trước khi tạo yêu cầu hủy.");

            tracker.MoveTo(CommerceFlowStage.ValidateBusinessRules, order.OrderStatus.ToString())
                .AddMetadata("FulfillmentStatus", order.FulfillmentStatus);
            var ineligibility = GetRequestIneligibilityMessage(order);
            if (ineligibility is not null)
            {
                throw new OrderCancellationException(ineligibility);
            }

            var requestedItemIds = normalized.Lines
                .Select(item => item.OrderItemId)
                .ToArray();
            var orderItems = order.Items
                .Where(item => requestedItemIds.Contains(item.Id))
                .ToDictionary(item => item.Id);

            if (orderItems.Count != normalized.Lines.Count)
            {
                throw new OrderCancellationException(
                    "Yêu cầu chứa sản phẩm không thuộc đơn hàng.");
            }

            var quantityRows = await _context.Set<OrderCancellationItem>()
                .AsNoTracking()
                .Where(item =>
                    item.CancellationRequest.OrderId == order.Id
                    && requestedItemIds.Contains(item.OrderItemId)
                    && (item.CancellationRequest.Status == OrderCancellationStatus.Pending
                        || item.CancellationRequest.Status == OrderCancellationStatus.Approved))
                .Select(item => new
                {
                    item.OrderItemId,
                    PendingQuantity = item.CancellationRequest.Status
                        == OrderCancellationStatus.Pending
                        ? item.RequestedQuantity
                        : 0,
                    ApprovedQuantity = item.CancellationRequest.Status
                        == OrderCancellationStatus.Approved
                        ? item.ApprovedQuantity
                        : 0
                })
                .ToArrayAsync(cancellationToken);

            var pendingByItem = quantityRows
                .GroupBy(item => item.OrderItemId)
                .ToDictionary(
                    group => group.Key,
                    group => group.Sum(item => item.PendingQuantity));
            var approvedByItem = quantityRows
                .GroupBy(item => item.OrderItemId)
                .ToDictionary(
                    group => group.Key,
                    group => group.Sum(item => item.ApprovedQuantity));

            foreach (var line in normalized.Lines)
            {
                var item = orderItems[line.OrderItemId];
                var available = item.Quantity
                    - pendingByItem.GetValueOrDefault(item.Id)
                    - approvedByItem.GetValueOrDefault(item.Id);

                if (line.Quantity > available)
                {
                    throw new OrderCancellationException(
                        $"SKU {item.Sku} chỉ còn {Math.Max(0, available)} sản phẩm có thể yêu cầu hủy.");
                }
            }

            tracker.MoveTo(CommerceFlowStage.ApplyStateTransition);
            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var request = new OrderCancellationRequest
            {
                OrderId = order.Id,
                RequestedBy = normalized.RequestedBy,
                ReasonCode = normalized.ReasonCode,
                ReasonText = normalized.ReasonText,
                Status = OrderCancellationStatus.Pending,
                IdempotencyKey = normalized.IdempotencyKey,
                RequestedAt = nowUtc,
                CreatedAt = nowUtc
            };

            foreach (var line in normalized.Lines)
            {
                request.Items.Add(new OrderCancellationItem
                {
                    OrderItemId = line.OrderItemId,
                    RequestedQuantity = line.Quantity,
                    ApprovedQuantity = 0,
                    RefundAmount = 0m,
                    CreatedAt = nowUtc
                });
            }

            _context.Set<OrderCancellationRequest>().Add(request);
            tracker.MoveTo(CommerceFlowStage.WriteTimeline);
            _context.OrderStatusHistories.Add(new OrderStatusHistory
            {
                OrderId = order.Id,
                Category = OrderHistoryCategory.Order,
                FromStatus = order.OrderStatus.ToString(),
                ToStatus = order.OrderStatus.ToString(),
                Code = "CANCELLATION_REQUESTED",
                Title = "Đã tạo yêu cầu hủy đơn",
                Description = $"{normalized.ReasonCode}: {normalized.ReasonText}",
                ChangedBy = normalized.RequestedBy,
                CustomerVisible = true,
                OccurredAt = nowUtc,
                CorrelationId = ToCorrelationId(normalized.IdempotencyKey)
            });

            tracker.MoveTo(CommerceFlowStage.SaveChanges);
            await _context.SaveChangesAsync(cancellationToken);
            tracker.MoveTo(CommerceFlowStage.Commit);
            await transaction.CommitAsync(cancellationToken);
            tracker.MoveTo(CommerceFlowStage.Complete);

            _context.ChangeTracker.Clear();
            return await GetSummaryAsync(order.Id, cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await RollbackAsync(transaction, tracker, cancellationToken);
            throw new OrderCancellationConcurrencyException(
                "Đơn hàng vừa được cập nhật bởi thao tác khác. Vui lòng tải lại.",
                tracker.Snapshot(),
                exception);
        }
        catch (OrderCancellationConcurrencyException exception)
        {
            await RollbackAsync(transaction, tracker, cancellationToken);
            throw new OrderCancellationConcurrencyException(
                exception.DetailMessage,
                tracker.MoveTo(CommerceFlowStage.ValidateConcurrency).Snapshot(),
                exception);
        }
        catch (OrderCancellationException exception)
        {
            await RollbackAsync(transaction, tracker, cancellationToken);
            throw new OrderCancellationException(
                exception.ErrorCode,
                exception.DetailMessage,
                tracker.Snapshot(),
                exception);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await RollbackAsync(transaction, tracker, CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            await RollbackAsync(transaction, tracker, cancellationToken);
            throw new OrderCancellationException(
                "CANCELLATION_REQUEST_TRANSACTION_FAILED",
                "Không thể hoàn tất transaction tạo yêu cầu hủy.",
                tracker.Snapshot(),
                exception);
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    public async Task<OrderCancellationSummary> ReviewAsync(
        ReviewOrderCancellationCommand command,
        CancellationToken cancellationToken)
    {
        var normalized = NormalizeReviewCommand(command);
        var tracker = new CommerceFlowTracker(
            "OrderCancellationReview",
            nameof(OrderCancellationRequest),
            normalized.CancellationRequestId.ToString(CultureInfo.InvariantCulture),
            normalized.Approve ? "ApproveCancellation" : "RejectCancellation",
            correlationId: $"cancel-review:{normalized.CancellationRequestId}",
            idempotencyKey: $"cancel-review:{normalized.CancellationRequestId}")
            .AddMetadata("OrderId", normalized.OrderId);

        IDbContextTransaction? transaction = null;
        try
        {
            tracker.MoveTo(CommerceFlowStage.BeginTransaction);
            transaction = await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            tracker.MoveTo(CommerceFlowStage.LoadAggregate);
            var request = await _context.Set<OrderCancellationRequest>()
                .Include(item => item.Items)
                    .ThenInclude(item => item.OrderItem)
                .SingleOrDefaultAsync(
                    item => item.Id == normalized.CancellationRequestId
                        && item.OrderId == normalized.OrderId,
                    cancellationToken)
                ?? throw new OrderCancellationException(
                    "Không tìm thấy yêu cầu hủy của đơn hàng.");

            var targetStatus = normalized.Approve
                ? OrderCancellationStatus.Approved
                : OrderCancellationStatus.Rejected;

            if (request.Status != OrderCancellationStatus.Pending)
            {
                if (request.Status == targetStatus)
                {
                    tracker.MoveTo(CommerceFlowStage.Commit);
                    await transaction.CommitAsync(cancellationToken);
                    tracker.MoveTo(CommerceFlowStage.Complete);
                    _context.ChangeTracker.Clear();
                    return await GetSummaryAsync(normalized.OrderId, cancellationToken);
                }

                throw new OrderCancellationException(
                    $"Yêu cầu đã ở trạng thái {request.Status} và không thể duyệt lại.");
            }

            tracker.MoveTo(CommerceFlowStage.ValidateConcurrency, request.Status.ToString());
            EnsureRowVersion(
                request.RowVersion,
                normalized.CancellationRowVersion,
                "Yêu cầu hủy vừa được cập nhật. Vui lòng tải lại.");

            tracker.MoveTo(CommerceFlowStage.LoadAggregate, request.Status.ToString());
            var order = await _context.Orders
                .Include(item => item.Items)
                .Include(item => item.PaymentTransactions)
                .Include(item => item.Shipments)
                .SingleOrDefaultAsync(item => item.Id == normalized.OrderId, cancellationToken)
                ?? throw new OrderCancellationException(
                    $"Không tìm thấy đơn hàng {normalized.OrderId}.");

            tracker.MoveTo(CommerceFlowStage.ValidateConcurrency, order.OrderStatus.ToString());
            EnsureRowVersion(
                order.RowVersion,
                normalized.OrderRowVersion,
                "Đơn hàng vừa được cập nhật. Vui lòng tải lại.");

            _context.Entry(order)
                .Property(item => item.RowVersion)
                .OriginalValue = normalized.OrderRowVersion;
            _context.Entry(request)
                .Property(item => item.RowVersion)
                .OriginalValue = normalized.CancellationRowVersion;

            tracker.MoveTo(CommerceFlowStage.ValidateBusinessRules, order.OrderStatus.ToString())
                .AddMetadata("CancellationStatus", request.Status)
                .AddMetadata("FulfillmentStatus", order.FulfillmentStatus);
            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

            if (!normalized.Approve)
            {
                tracker.MoveTo(CommerceFlowStage.ApplyStateTransition, request.Status.ToString());
                request.Status = OrderCancellationStatus.Rejected;
                request.ReviewedAt = nowUtc;
                request.ReviewedBy = normalized.ReviewedBy;
                request.ReviewNote = normalized.ReviewNote;
                request.UpdatedAt = nowUtc;

                tracker.MoveTo(CommerceFlowStage.WriteTimeline);
                _context.OrderStatusHistories.Add(new OrderStatusHistory
                {
                    OrderId = order.Id,
                    Category = OrderHistoryCategory.Order,
                    FromStatus = "CancellationPending",
                    ToStatus = "CancellationRejected",
                    Code = "CANCELLATION_REJECTED",
                    Title = "Yêu cầu hủy bị từ chối",
                    Description = normalized.ReviewNote,
                    ChangedBy = normalized.ReviewedBy,
                    CustomerVisible = true,
                    OccurredAt = nowUtc,
                    CorrelationId = ToCorrelationId(request.IdempotencyKey)
                });

                tracker.MoveTo(CommerceFlowStage.SaveChanges);
                await _context.SaveChangesAsync(cancellationToken);
                tracker.MoveTo(CommerceFlowStage.Commit);
                await transaction.CommitAsync(cancellationToken);
                tracker.MoveTo(CommerceFlowStage.Complete);
                _context.ChangeTracker.Clear();
                return await GetSummaryAsync(order.Id, cancellationToken);
            }

            tracker.MoveTo(CommerceFlowStage.ValidateBusinessRules, order.OrderStatus.ToString());
            var ineligibility = GetApprovalIneligibilityMessage(order);
            if (ineligibility is not null)
            {
                throw new OrderCancellationException(ineligibility);
            }

            await ValidateShipmentCreateOutboxAsync(order, tracker, cancellationToken);

            var requestedItemIds = request.Items
                .Select(item => item.OrderItemId)
                .ToArray();

            var approvedRows = await _context.Set<OrderCancellationItem>()
                .AsNoTracking()
                .Where(item =>
                    item.CancellationRequest.OrderId == order.Id
                    && item.CancellationRequestId != request.Id
                    && item.CancellationRequest.Status == OrderCancellationStatus.Approved
                    && requestedItemIds.Contains(item.OrderItemId))
                .GroupBy(item => item.OrderItemId)
                .Select(group => new
                {
                    OrderItemId = group.Key,
                    Quantity = group.Sum(item => item.ApprovedQuantity)
                })
                .ToDictionaryAsync(item => item.OrderItemId, item => item.Quantity, cancellationToken);

            var orderItems = order.Items.ToDictionary(item => item.Id);
            foreach (var cancellationItem in request.Items)
            {
                if (!orderItems.TryGetValue(cancellationItem.OrderItemId, out var orderItem))
                {
                    throw new OrderCancellationException(
                        "Yêu cầu hủy chứa sản phẩm không còn thuộc đơn hàng.");
                }

                var available = orderItem.Quantity
                    - approvedRows.GetValueOrDefault(orderItem.Id);
                if (cancellationItem.RequestedQuantity > available)
                {
                    throw new OrderCancellationException(
                        $"SKU {orderItem.Sku} chỉ còn {Math.Max(0, available)} sản phẩm có thể hủy.");
                }
            }

            var inventoryLines = request.Items
                .Select(item => new CancellationInventoryLine(
                    item.Id,
                    item.OrderItemId,
                    item.OrderItem.ProductVariantId,
                    item.RequestedQuantity))
                .ToArray();

            tracker.MoveTo(CommerceFlowStage.WriteInventoryLedger);
            await _inventoryService.CompensateCancellationAsync(
                order.Id,
                inventoryLines,
                request.ReasonText,
                $"cancel:{request.Id}",
                normalized.ReviewedBy,
                cancellationToken);

            var cancellationBases = request.Items
                .ToDictionary(
                    item => item.Id,
                    item => RoundMoney(
                        item.OrderItem.UnitPrice * item.RequestedQuantity));
            var cancellationBaseTotal = cancellationBases.Values.Sum();
            var currentSubtotal = order.Subtotal;

            if (cancellationBaseTotal > currentSubtotal + 0.01m)
            {
                throw new OrderCancellationException(
                    "Giá trị hủy vượt tạm tính còn lại của đơn hàng.");
            }

            var allocatedDiscount = AllocateProportion(
                order.DiscountTotal,
                cancellationBaseTotal,
                currentSubtotal);
            var allocatedTax = AllocateProportion(
                order.TaxTotal,
                cancellationBaseTotal,
                currentSubtotal);

            var approvedAfter = approvedRows.ToDictionary(item => item.Key, item => item.Value);
            foreach (var item in request.Items)
            {
                approvedAfter[item.OrderItemId] =
                    approvedAfter.GetValueOrDefault(item.OrderItemId)
                    + item.RequestedQuantity;
            }

            var previousOrderStatus = order.OrderStatus;
            var remainingQuantity = order.Items.Sum(item =>
                Math.Max(0, item.Quantity - approvedAfter.GetValueOrDefault(item.Id)));
            var fullCancellation = remainingQuantity == 0;
            var shippingRefund = fullCancellation ? order.ShippingFee : 0m;
            var refundTotal = Math.Max(
                0m,
                RoundMoney(
                    cancellationBaseTotal
                    - allocatedDiscount
                    + allocatedTax
                    + shippingRefund));

            DistributeRefund(request.Items, cancellationBases, refundTotal);

            tracker.MoveTo(CommerceFlowStage.ApplyStateTransition, request.Status.ToString());
            request.Status = OrderCancellationStatus.Approved;
            request.ReviewedAt = nowUtc;
            request.ReviewedBy = normalized.ReviewedBy;
            request.ReviewNote = normalized.ReviewNote;
            request.UpdatedAt = nowUtc;

            foreach (var item in request.Items)
            {
                item.ApprovedQuantity = item.RequestedQuantity;
            }

            order.Subtotal = Math.Max(0m, RoundMoney(order.Subtotal - cancellationBaseTotal));
            order.DiscountTotal = Math.Max(0m, RoundMoney(order.DiscountTotal - allocatedDiscount));
            order.TaxTotal = Math.Max(0m, RoundMoney(order.TaxTotal - allocatedTax));

            if (fullCancellation)
            {
                order.ShippingFee = 0m;
            }

            order.GrandTotal = Math.Max(
                0m,
                RoundMoney(
                    order.Subtotal
                    + order.ShippingFee
                    + order.TaxTotal
                    - order.DiscountTotal));
            order.UpdatedAt = nowUtc;

            tracker.MoveTo(CommerceFlowStage.UpdateRelatedAggregate);
            await ApplyShipmentCancellationAsync(
                order,
                request,
                fullCancellation,
                nowUtc,
                cancellationBaseTotal,
                cancellationToken);

            if (fullCancellation)
            {
                order.OrderStatus = OrderStatus.Cancelled;
                order.FulfillmentStatus = FulfillmentStatus.Cancelled;
                order.CancelledAt = nowUtc;
                order.CancelReason = request.ReasonText;

                if (order.PaymentStatus is PaymentStatus.Pending or PaymentStatus.CodPending)
                {
                    order.PaymentStatus = PaymentStatus.Cancelled;
                    foreach (var payment in order.PaymentTransactions.Where(item =>
                                 item.Status is PaymentStatus.Pending or PaymentStatus.CodPending))
                    {
                        payment.Status = PaymentStatus.Cancelled;
                        payment.CompletedAt ??= nowUtc;
                        payment.UpdatedAt = nowUtc;
                    }
                }
            }

            if (order.PaymentStatus == PaymentStatus.Paid && refundTotal > 0m)
            {
                tracker.MoveTo(CommerceFlowStage.WriteOutbox);
                await AddRefundOutboxAsync(
                    order,
                    request,
                    refundTotal,
                    normalized.ReviewedBy,
                    nowUtc,
                    cancellationToken);

                _context.OrderStatusHistories.Add(new OrderStatusHistory
                {
                    OrderId = order.Id,
                    Category = OrderHistoryCategory.Payment,
                    FromStatus = PaymentStatus.Paid.ToString(),
                    ToStatus = "RefundPending",
                    Code = "REFUND_REQUESTED",
                    Title = "Đã tạo yêu cầu hoàn tiền",
                    Description = $"Số tiền chờ hoàn: {refundTotal.ToString("0.00", CultureInfo.InvariantCulture)} {order.Currency}.",
                    ChangedBy = normalized.ReviewedBy,
                    CustomerVisible = true,
                    OccurredAt = nowUtc,
                    CorrelationId = ToCorrelationId(request.IdempotencyKey)
                });
            }

            tracker.MoveTo(CommerceFlowStage.WriteTimeline);
            _context.OrderStatusHistories.Add(new OrderStatusHistory
            {
                OrderId = order.Id,
                Category = OrderHistoryCategory.Order,
                FromStatus = fullCancellation
                    ? previousOrderStatus.ToString()
                    : "PartiallyActive",
                ToStatus = fullCancellation
                    ? OrderStatus.Cancelled.ToString()
                    : "PartiallyCancelled",
                Code = fullCancellation
                    ? "ORDER_CANCELLED"
                    : "ORDER_PARTIALLY_CANCELLED",
                Title = fullCancellation
                    ? "Đơn hàng đã được hủy"
                    : "Đã hủy một phần đơn hàng",
                Description =
                    $"{request.Items.Sum(item => item.ApprovedQuantity)} sản phẩm. {request.ReasonText}",
                ChangedBy = normalized.ReviewedBy,
                CustomerVisible = true,
                OccurredAt = nowUtc,
                CorrelationId = ToCorrelationId(request.IdempotencyKey)
            });

            tracker.MoveTo(CommerceFlowStage.SaveChanges);
            await _context.SaveChangesAsync(cancellationToken);
            tracker.MoveTo(CommerceFlowStage.Commit);
            await transaction.CommitAsync(cancellationToken);
            tracker.MoveTo(CommerceFlowStage.Complete);

            _context.ChangeTracker.Clear();
            return await GetSummaryAsync(order.Id, cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await RollbackAsync(transaction, tracker, cancellationToken);

            _logger.LogWarning(
                exception,
                "Cancellation review for request {CancellationRequestId} lost a concurrency race at {Stage}. CorrelationId={CorrelationId}",
                command.CancellationRequestId,
                tracker.Stage,
                tracker.CorrelationId);

            throw new OrderCancellationConcurrencyException(
                "Đơn hàng hoặc yêu cầu hủy vừa được cập nhật. Vui lòng tải lại.",
                tracker.Snapshot(),
                exception);
        }
        catch (OrderCancellationConcurrencyException exception)
        {
            await RollbackAsync(transaction, tracker, cancellationToken);
            throw new OrderCancellationConcurrencyException(
                exception.DetailMessage,
                tracker.MoveTo(CommerceFlowStage.ValidateConcurrency).Snapshot(),
                exception);
        }
        catch (OrderCancellationException exception)
        {
            await RollbackAsync(transaction, tracker, cancellationToken);
            throw new OrderCancellationException(
                exception.ErrorCode,
                exception.DetailMessage,
                tracker.Snapshot(),
                exception);
        }
        catch (InventoryConflictException exception)
        {
            await RollbackAsync(transaction, tracker, cancellationToken);
            throw new OrderCancellationException(
                "INVENTORY_COMPENSATION_CONFLICT",
                exception.Message,
                tracker.MoveTo(CommerceFlowStage.WriteInventoryLedger).Snapshot(),
                exception);
        }
        catch (InventoryValidationException exception)
        {
            await RollbackAsync(transaction, tracker, cancellationToken);
            throw new OrderCancellationException(
                "INVENTORY_COMPENSATION_INVALID",
                exception.Message,
                tracker.MoveTo(CommerceFlowStage.WriteInventoryLedger).Snapshot(),
                exception);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await RollbackAsync(transaction, tracker, CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            await RollbackAsync(transaction, tracker, cancellationToken);
            throw new OrderCancellationException(
                "CANCELLATION_REVIEW_TRANSACTION_FAILED",
                "Không thể hoàn tất transaction duyệt yêu cầu hủy.",
                tracker.Snapshot(),
                exception);
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private async Task ValidateShipmentCreateOutboxAsync(
        Order order,
        CommerceFlowTracker tracker,
        CancellationToken cancellationToken)
    {
        var localShipments = order.Shipments
            .Where(item => item.Direction == ShipmentDirection.Outbound
                && string.IsNullOrWhiteSpace(item.ExternalOrderCode))
            .ToArray();
        if (localShipments.Length == 0)
        {
            return;
        }

        var keys = localShipments
            .Select(item => $"ghn:create:shipment:{item.Id}")
            .ToArray();
        tracker.MoveTo(CommerceFlowStage.Deduplicate)
            .AddMetadata("OutboundCreateKeys", string.Join(",", keys));

        var messages = await _context.IntegrationOutboxMessages
            .AsNoTracking()
            .Where(item => item.Provider == "GHN"
                && keys.Contains(item.IdempotencyKey))
            .ToArrayAsync(cancellationToken);

        var processing = messages.FirstOrDefault(item =>
            item.Status == IntegrationOutboxStatus.Processing);
        if (processing is not null)
        {
            throw new OrderCancellationException(
                "CANCELLATION_BLOCKED_BY_SHIPMENT_CREATE_IN_PROGRESS",
                $"Outbox {processing.Id} đang gọi GHN tạo vận đơn. Chờ create flow kết thúc hoặc reconcile trước khi duyệt hủy.",
                tracker.Snapshot());
        }

        var inconsistent = messages.FirstOrDefault(item =>
            item.Status == IntegrationOutboxStatus.Completed);
        if (inconsistent is not null)
        {
            throw new OrderCancellationException(
                "CANCELLATION_BLOCKED_BY_SHIPMENT_CREATE_INCONSISTENCY",
                $"Outbox {inconsistent.Id} đã Completed nhưng shipment chưa có mã provider. Cần reconcile trước khi duyệt hủy.",
                tracker.Snapshot());
        }
    }

    private async Task ApplyShipmentCancellationAsync(
        Order order,
        OrderCancellationRequest request,
        bool fullCancellation,
        DateTime nowUtc,
        decimal cancellationBaseTotal,
        CancellationToken cancellationToken)
    {
        var outboundShipments = order.Shipments
            .Where(item => item.Direction == ShipmentDirection.Outbound)
            .OrderByDescending(item => item.Id)
            .ToArray();

        var sourceShipment = outboundShipments.FirstOrDefault();
        var locallyCancellable = outboundShipments
            .Where(item => string.IsNullOrWhiteSpace(item.ExternalOrderCode)
                && item.Status is ShipmentStatus.Draft
                    or ShipmentStatus.PendingCreation
                    or ShipmentStatus.Created)
            .ToArray();
        var localCreateKeys = locallyCancellable
            .Select(item => $"ghn:create:shipment:{item.Id}")
            .ToArray();
        var localCreateMessages = localCreateKeys.Length == 0
            ? Array.Empty<IntegrationOutboxMessage>()
            : await _context.IntegrationOutboxMessages
                .Where(item => item.Provider == "GHN"
                    && localCreateKeys.Contains(item.IdempotencyKey))
                .ToArrayAsync(cancellationToken);

        foreach (var shipment in locallyCancellable)
        {
            shipment.Status = ShipmentStatus.Cancelled;
            shipment.ProviderStatus = "cancelled_before_provider_handoff";
            shipment.ProviderReason = request.ReasonText;
            shipment.CancelRequestedAt ??= nowUtc;
            shipment.CancelledAt ??= nowUtc;
            shipment.UpdatedAt = nowUtc;

            var createKey = $"ghn:create:shipment:{shipment.Id}";
            var createMessage = localCreateMessages.FirstOrDefault(item =>
                item.IdempotencyKey == createKey);
            if (createMessage is not null
                && createMessage.Status is IntegrationOutboxStatus.Pending
                    or IntegrationOutboxStatus.Failed)
            {
                createMessage.Status = IntegrationOutboxStatus.Completed;
                createMessage.CompletedAt = nowUtc;
                createMessage.NextAttemptAt = null;
                createMessage.LockedAt = null;
                createMessage.LastError =
                    "Cancellation approval stopped shipment creation before provider handoff.";
                createMessage.UpdatedAt = nowUtc;
            }
        }

        var unconfirmedProviderShipment = outboundShipments.FirstOrDefault(item =>
            !string.IsNullOrWhiteSpace(item.ExternalOrderCode)
            && item.Status != ShipmentStatus.Cancelled);
        if (unconfirmedProviderShipment is not null)
        {
            throw new OrderCancellationException(
                "CANCELLATION_REQUIRES_PROVIDER_CONFIRMATION: " +
                $"Vận đơn {unconfirmedProviderShipment.ExternalOrderCode} phải được GHN xác nhận Cancelled trước khi hoàn kho hoặc duyệt hủy.");
        }

        if (!fullCancellation && sourceShipment is not null)
        {
            if (locallyCancellable.Length > 0)
            {
                // Persist the old shipment cancellation first so the filtered unique
                // active-outbound index cannot race the replacement insert.
                await _context.SaveChangesAsync(cancellationToken);
            }

            order.Shipments.Add(new Shipment
            {
                Direction = ShipmentDirection.Outbound,
                Provider = sourceShipment.Provider,
                Status = ShipmentStatus.Draft,
                Fee = order.ShippingFee,
                CodAmount = order.PaymentStatus == PaymentStatus.CodPending
                    ? order.GrandTotal
                    : 0m,
                WeightGram = sourceShipment.WeightGram,
                LengthCm = sourceShipment.LengthCm,
                WidthCm = sourceShipment.WidthCm,
                HeightCm = sourceShipment.HeightCm,
                ProviderReason =
                    $"Tạo lại sau khi hủy một phần giá trị {cancellationBaseTotal:0.00}.",
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            });
        }

        await Task.CompletedTask;
    }

    private async Task AddRefundOutboxAsync(
        Order order,
        OrderCancellationRequest request,
        decimal refundTotal,
        string actor,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var payment = order.PaymentTransactions
            .Where(item => item.Status == PaymentStatus.Paid)
            .OrderByDescending(item => item.AttemptNumber)
            .ThenByDescending(item => item.Id)
            .FirstOrDefault();

        var provider = payment?.Provider ?? "Internal";
        var idempotencyKey = $"cancel:{request.Id}:refund";
        var exists = await _context.IntegrationOutboxMessages
            .AsNoTracking()
            .AnyAsync(item =>
                item.Provider == provider
                && item.IdempotencyKey == idempotencyKey,
                cancellationToken);

        if (exists)
        {
            return;
        }

        _context.IntegrationOutboxMessages.Add(new IntegrationOutboxMessage
        {
            Provider = provider,
            MessageType = "RefundRequested",
            AggregateType = "OrderCancellation",
            AggregateId = request.Id.ToString(CultureInfo.InvariantCulture),
            IdempotencyKey = idempotencyKey,
            Status = IntegrationOutboxStatus.Pending,
            Payload = JsonSerializer.Serialize(new
            {
                orderId = order.Id,
                orderCode = order.Code,
                cancellationRequestId = request.Id,
                paymentTransactionId = payment?.Id,
                providerTransactionId = payment?.ProviderTransactionId,
                amount = refundTotal,
                currency = order.Currency,
                requestedBy = actor
            }),
            AttemptCount = 0,
            CorrelationId = ToCorrelationId(request.IdempotencyKey),
            CreatedAt = nowUtc
        });
    }

    private static async Task RollbackAsync(
        IDbContextTransaction? transaction,
        CommerceFlowTracker tracker,
        CancellationToken cancellationToken)
    {
        if (transaction is null)
        {
            return;
        }

        var failedStage = tracker.Stage;
        try
        {
            tracker.MoveTo(CommerceFlowStage.Rollback, failedStage.ToString());
            await transaction.RollbackAsync(cancellationToken);
        }
        catch (Exception rollbackException)
        {
            tracker.AddMetadata("RollbackException", rollbackException.Message);
        }
        finally
        {
            tracker.MoveTo(failedStage);
        }
    }

    private static CreateOrderCancellationCommand NormalizeCreateCommand(
        CreateOrderCancellationCommand command)
    {
        if (command.OrderId <= 0)
        {
            throw new OrderCancellationException("Mã đơn hàng không hợp lệ.");
        }

        if (command.OrderRowVersion is not { Length: > 0 })
        {
            throw new OrderCancellationException("RowVersion của đơn hàng là bắt buộc.");
        }

        if (command.Lines is null || command.Lines.Count == 0)
        {
            throw new OrderCancellationException(
                "Hãy chọn ít nhất một sản phẩm cần hủy.");
        }

        if (command.Lines.Any(item => item.OrderItemId <= 0 || item.Quantity <= 0))
        {
            throw new OrderCancellationException(
                "Danh sách hủy chứa mã sản phẩm hoặc số lượng không hợp lệ.");
        }

        var duplicate = command.Lines
            .GroupBy(item => item.OrderItemId)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new OrderCancellationException(
                $"Order item {duplicate.Key} xuất hiện nhiều lần.");
        }

        return command with
        {
            ReasonCode = Normalize(
                command.ReasonCode,
                ReasonCodeMaxLength,
                nameof(command.ReasonCode)),
            ReasonText = Normalize(
                command.ReasonText,
                ReasonTextMaxLength,
                nameof(command.ReasonText)),
            RequestedBy = Normalize(
                command.RequestedBy,
                ActorMaxLength,
                nameof(command.RequestedBy)),
            IdempotencyKey = Normalize(
                command.IdempotencyKey,
                IdempotencyKeyMaxLength,
                nameof(command.IdempotencyKey)),
            Lines = command.Lines
                .OrderBy(item => item.OrderItemId)
                .ToArray()
        };
    }

    private static ReviewOrderCancellationCommand NormalizeReviewCommand(
        ReviewOrderCancellationCommand command)
    {
        if (command.OrderId <= 0 || command.CancellationRequestId <= 0)
        {
            throw new OrderCancellationException(
                "Mã đơn hàng hoặc yêu cầu hủy không hợp lệ.");
        }

        if (command.OrderRowVersion is not { Length: > 0 }
            || command.CancellationRowVersion is not { Length: > 0 })
        {
            throw new OrderCancellationException(
                "RowVersion của đơn hàng và yêu cầu hủy là bắt buộc.");
        }

        return command with
        {
            ReviewedBy = Normalize(
                command.ReviewedBy,
                ActorMaxLength,
                nameof(command.ReviewedBy)),
            ReviewNote = NormalizeOptional(command.ReviewNote, ReviewNoteMaxLength)
        };
    }

    private static void ValidateIdempotentRequest(
        OrderCancellationRequest existing,
        CreateOrderCancellationCommand requested)
    {
        var existingLines = existing.Items
            .OrderBy(item => item.OrderItemId)
            .Select(item => new CancellationRequestLineCommand(
                item.OrderItemId,
                item.RequestedQuantity))
            .ToArray();

        if (existing.OrderId != requested.OrderId
            || !string.Equals(
                existing.ReasonCode,
                requested.ReasonCode,
                StringComparison.Ordinal)
            || !string.Equals(
                existing.ReasonText,
                requested.ReasonText,
                StringComparison.Ordinal)
            || !existingLines.SequenceEqual(requested.Lines))
        {
            throw new OrderCancellationException(
                "Idempotency key đã được dùng cho một yêu cầu hủy khác.");
        }
    }

    private static string? GetRequestIneligibilityMessage(Order order)
    {
        if (order.OrderStatus is
            OrderStatus.Completed
            or OrderStatus.Cancelled
            or OrderStatus.Closed)
        {
            return $"Đơn ở trạng thái {order.OrderStatus} không thể hủy theo luồng trước giao hàng.";
        }

        if (order.OrderStatus is not
            OrderStatus.PendingPayment
            and not OrderStatus.Placed
            and not OrderStatus.Confirmed
            and not OrderStatus.Processing)
        {
            return $"Đơn ở trạng thái {order.OrderStatus} chưa được hỗ trợ hủy.";
        }

        if (order.FulfillmentStatus is
            FulfillmentStatus.Shipped
            or FulfillmentStatus.Delivered
            or FulfillmentStatus.DeliveryFailed
            or FulfillmentStatus.Returning
            or FulfillmentStatus.Returned)
        {
            return "CANCELLATION_AFTER_CARRIER_HANDOFF: Đơn đã bàn giao vận chuyển. Hãy dùng return workflow sau khi đủ điều kiện.";
        }

        var outbound = order.Shipments
            .Where(item => item.Direction == ShipmentDirection.Outbound)
            .ToArray();

        if (outbound.Any(item => item.CarrierHandoffAt.HasValue
            || HandoverShipmentStatuses.Contains(item.Status)))
        {
            return "CANCELLATION_AFTER_CARRIER_HANDOFF: GHN đã tiếp nhận hoặc phát sinh giao hàng; không thể dùng cancellation workflow.";
        }

        return null;
    }

    private static string? GetApprovalIneligibilityMessage(Order order)
    {
        var baseMessage = GetRequestIneligibilityMessage(order);
        if (baseMessage is not null)
        {
            return baseMessage;
        }

        var providerShipment = order.Shipments
            .Where(item => item.Direction == ShipmentDirection.Outbound)
            .OrderByDescending(item => item.Id)
            .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.ExternalOrderCode));

        if (providerShipment is null || providerShipment.Status == ShipmentStatus.Cancelled)
        {
            return null;
        }

        if (providerShipment.Status == ShipmentStatus.CancelRequested)
        {
            return "CANCELLATION_WAITING_PROVIDER_CONFIRMATION: Yêu cầu hủy vận đơn đã gửi GHN; chỉ được duyệt cancellation sau khi shipment thành Cancelled.";
        }

        return "CANCELLATION_REQUIRES_PROVIDER_CANCELLATION: Vận đơn đã có mã GHN. Hãy gửi yêu cầu hủy vận đơn và chờ provider xác nhận trước khi duyệt hủy/hoàn kho.";
    }

    private static OrderCancellationRequestSummary ToRequestSummary(
        OrderCancellationRequest request)
    {
        return new OrderCancellationRequestSummary(
            request.Id,
            request.Status.ToString(),
            request.ReasonCode,
            request.ReasonText,
            request.RequestedBy,
            request.RequestedAt,
            request.ReviewedAt,
            request.ReviewedBy,
            request.ReviewNote,
            Convert.ToBase64String(request.RowVersion),
            request.Items.Sum(item => item.RefundAmount),
            request.Items
                .OrderBy(item => item.Id)
                .Select(item => new OrderCancellationItemSummary(
                    item.Id,
                    item.OrderItemId,
                    item.OrderItem.ProductName,
                    item.OrderItem.Sku,
                    item.RequestedQuantity,
                    item.ApprovedQuantity,
                    item.RefundAmount))
                .ToArray());
    }

    private static void DistributeRefund(
        ICollection<OrderCancellationItem> items,
        IReadOnlyDictionary<long, decimal> cancellationBases,
        decimal refundTotal)
    {
        var ordered = items.OrderBy(item => item.Id).ToArray();
        var baseTotal = cancellationBases.Values.Sum();
        var distributed = 0m;

        for (var index = 0; index < ordered.Length; index++)
        {
            var item = ordered[index];
            decimal amount;
            if (index == ordered.Length - 1)
            {
                amount = Math.Max(0m, refundTotal - distributed);
            }
            else
            {
                amount = baseTotal <= 0m
                    ? 0m
                    : RoundMoney(
                        refundTotal
                        * cancellationBases[item.Id]
                        / baseTotal);
                distributed += amount;
            }

            item.RefundAmount = amount;
        }
    }

    private static decimal AllocateProportion(
        decimal total,
        decimal selectedBase,
        decimal currentBase)
    {
        if (total <= 0m || selectedBase <= 0m || currentBase <= 0m)
        {
            return 0m;
        }

        return Math.Min(
            total,
            RoundMoney(total * selectedBase / currentBase));
    }

    private static decimal RoundMoney(decimal value)
    {
        return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    private static void EnsureRowVersion(
        byte[] current,
        byte[] supplied,
        string message)
    {
        if (!current.AsSpan().SequenceEqual(supplied))
        {
            throw new OrderCancellationConcurrencyException(message);
        }
    }

    private static string ToCorrelationId(string value)
    {
        var normalized = value.Trim();
        return normalized.Length <= 64
            ? normalized
            : normalized[..64];
    }

    private static string Normalize(string value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new OrderCancellationException($"{parameterName} không được để trống.");
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new OrderCancellationException(
                $"{parameterName} không được vượt {maxLength} ký tự.");
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
        if (normalized.Length > maxLength)
        {
            throw new OrderCancellationException(
                $"ReviewNote không được vượt {maxLength} ký tự.");
        }

        return normalized;
    }
}
