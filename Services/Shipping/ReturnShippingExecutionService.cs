using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Commerce.Flows;
using WebApplication2.Services.Shipping.Ghn;

namespace WebApplication2.Services.Shipping;

public sealed class ReturnShippingExecutionService : IReturnShippingService
{
    public const string CreateMessageType = "ReturnShipmentCreateRequested";

    private readonly ApplicationDbContext _context;
    private readonly IShippingGateway _gateway;
    private readonly GhnShippingOptions _options;
    private readonly TimeProvider _timeProvider;

    public ReturnShippingExecutionService(
        ApplicationDbContext context,
        IShippingGateway gateway,
        IOptions<GhnShippingOptions> options,
        TimeProvider timeProvider)
    {
        _context = context;
        _gateway = gateway;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public async Task<ShippingOperationResult<IReadOnlyList<ShippingServiceOption>>> GetServicesAsync(
        long returnRequestId,
        CancellationToken cancellationToken)
    {
        var route = await _context.ReturnRequests
            .AsNoTracking()
            .Where(item => item.Id == returnRequestId)
            .Select(item => new
            {
                FromDistrictId = item.Order.ShippingDistrictId
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (route?.FromDistrictId is not > 0)
        {
            return ShippingOperationResult<IReadOnlyList<ShippingServiceOption>>.Failure(
                "RETURN_ADDRESS_INVALID",
                "Đơn hàng không có mã quận/huyện khách gửi trả hợp lệ.");
        }

        return await _gateway.GetAvailableServicesAsync(
            route.FromDistrictId.Value,
            _options.FromDistrictId,
            cancellationToken);
    }

    public async Task<ShippingOperationResult<ShippingQuote>> QuoteAsync(
        long returnRequestId,
        ReturnShippingExecutionInput input,
        CancellationToken cancellationToken)
    {
        var request = await LoadAggregateAsync(
            returnRequestId,
            tracked: false,
            cancellationToken);
        if (request is null)
        {
            return ShippingOperationResult<ShippingQuote>.Failure(
                "RETURN_REQUEST_NOT_FOUND",
                "Không tìm thấy yêu cầu hoàn trả.");
        }

        var error = ValidateInput(request, input);
        if (error is not null)
        {
            return ShippingOperationResult<ShippingQuote>.Failure(
                "RETURN_SHIPMENT_INPUT_INVALID",
                error);
        }

        return await _gateway.QuoteAsync(
            BuildQuoteRequest(request, input),
            cancellationToken);
    }

    public Task<ShippingQueueResult> QueueCreateAsync(
        long returnRequestId,
        ReturnShippingExecutionInput input,
        byte[] rowVersion,
        string actor,
        CancellationToken cancellationToken)
    {
        var tracker = new CommerceFlowTracker(
            "ReturnShipping",
            nameof(ReturnRequest),
            returnRequestId.ToString(CultureInfo.InvariantCulture),
            "QueueReturnShipmentCreate",
            idempotencyKey: $"ghn:return:create:{returnRequestId}");

        return CommerceFlowTransaction.ExecuteAsync(
            _context,
            tracker,
            async token =>
            {
                tracker.MoveTo(CommerceFlowStage.LoadAggregate);
                var request = await LoadAggregateAsync(
                    returnRequestId,
                    tracked: true,
                    token)
                    ?? throw new BusinessRuleViolationException(
                        "RETURN_REQUEST_NOT_FOUND",
                        "Không tìm thấy yêu cầu hoàn trả.",
                        tracker.Snapshot());

                tracker.MoveTo(
                    CommerceFlowStage.ValidateStateTransition,
                    request.Status.ToString());
                if (request.Status is not ReturnRequestStatus.Approved
                    and not ReturnRequestStatus.AwaitingReturnShipment)
                {
                    throw new InvalidStateTransitionException(
                        "RETURN_SHIPMENT_REQUIRES_APPROVED_REQUEST",
                        $"Chỉ tạo vận đơn trả hàng khi return ở Approved/AwaitingReturnShipment; hiện tại {request.Status}.",
                        tracker.Snapshot());
                }

                ApplyOriginalRowVersion(request, rowVersion, tracker);

                var error = ValidateInput(request, input);
                if (error is not null)
                {
                    throw new BusinessRuleViolationException(
                        "RETURN_SHIPMENT_INPUT_INVALID",
                        error,
                        tracker.MoveTo(CommerceFlowStage.ValidateBusinessRules).Snapshot());
                }

                var active = request.Shipments
                    .Where(item =>
                        item.Direction == ShipmentDirection.Return
                        && item.Status is not ShipmentStatus.Cancelled
                            and not ShipmentStatus.Returned)
                    .OrderByDescending(item => item.Id)
                    .FirstOrDefault();

                if (active is not null
                    && !string.IsNullOrWhiteSpace(active.ExternalOrderCode))
                {
                    return new ShippingQueueResult(
                        null,
                        true,
                        $"Return shipment đã có mã GHN {active.ExternalOrderCode}.");
                }

                var shipment = active;
                var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
                if (shipment is null)
                {
                    var outbound = request.Order.Shipments
                        .Where(item =>
                            item.Direction == ShipmentDirection.Outbound
                            && item.Status == ShipmentStatus.Delivered)
                        .OrderByDescending(item => item.DeliveredAt)
                        .ThenByDescending(item => item.Id)
                        .FirstOrDefault()
                        ?? throw new BusinessRuleViolationException(
                            "RETURN_OUTBOUND_SHIPMENT_NOT_DELIVERED",
                            "Không tìm thấy outbound shipment đã giao thành công.",
                            tracker.Snapshot());

                    shipment = new Shipment
                    {
                        OrderId = request.OrderId,
                        ReturnRequestId = request.Id,
                        ParentShipmentId = outbound.Id,
                        Direction = ShipmentDirection.Return,
                        Provider = _gateway.Provider,
                        Status = ShipmentStatus.PendingCreation,
                        ServiceCode = input.ServiceId.ToString(CultureInfo.InvariantCulture),
                        ServiceName = $"GHN service type {input.ServiceTypeId}",
                        Fee = 0,
                        CodAmount = 0,
                        WeightGram = input.WeightGram,
                        LengthCm = input.LengthCm,
                        WidthCm = input.WidthCm,
                        HeightCm = input.HeightCm,
                        CreatedAt = nowUtc
                    };
                    request.Shipments.Add(shipment);
                }
                else
                {
                    shipment.Status = ShipmentStatus.PendingCreation;
                    shipment.ServiceCode = input.ServiceId.ToString(CultureInfo.InvariantCulture);
                    shipment.ServiceName = $"GHN service type {input.ServiceTypeId}";
                    shipment.WeightGram = input.WeightGram;
                    shipment.LengthCm = input.LengthCm;
                    shipment.WidthCm = input.WidthCm;
                    shipment.HeightCm = input.HeightCm;
                    shipment.ProviderReason = null;
                    shipment.UpdatedAt = nowUtc;
                }

                request.Status = ReturnRequestStatus.AwaitingReturnShipment;
                request.UpdatedAt = nowUtc;

                tracker.MoveTo(CommerceFlowStage.SaveChanges);
                await _context.SaveChangesAsync(token);

                var key = $"ghn:return:create:{request.Id}:{shipment.Id}";
                var payload = JsonSerializer.Serialize(new GhnCreateReturnShipmentMessage(
                    request.Id,
                    shipment.Id,
                    input.ServiceId,
                    input.ServiceTypeId,
                    input.Note?.Trim()));

                tracker.MoveTo(CommerceFlowStage.WriteOutbox);
                var outbox = await _context.IntegrationOutboxMessages
                    .SingleOrDefaultAsync(
                        item => item.Provider == _gateway.Provider
                            && item.IdempotencyKey == key,
                        token);

                if (outbox is not null)
                {
                    if (outbox.Status is IntegrationOutboxStatus.Pending
                        or IntegrationOutboxStatus.Processing
                        or IntegrationOutboxStatus.Completed)
                    {
                        return new ShippingQueueResult(
                            outbox.Id,
                            true,
                            "Yêu cầu tạo vận đơn chiều về đã tồn tại.");
                    }

                    outbox.Status = IntegrationOutboxStatus.Pending;
                    outbox.Payload = payload;
                    outbox.NextAttemptAt = nowUtc;
                    outbox.LockedAt = null;
                    outbox.LastError = null;
                    outbox.UpdatedAt = nowUtc;
                }
                else
                {
                    outbox = new IntegrationOutboxMessage
                    {
                        Provider = _gateway.Provider,
                        MessageType = CreateMessageType,
                        AggregateType = nameof(ReturnRequest),
                        AggregateId = request.Id.ToString(CultureInfo.InvariantCulture),
                        IdempotencyKey = key,
                        Status = IntegrationOutboxStatus.Pending,
                        Payload = payload,
                        NextAttemptAt = nowUtc,
                        CorrelationId = tracker.CorrelationId,
                        CreatedAt = nowUtc
                    };
                    _context.IntegrationOutboxMessages.Add(outbox);
                }

                request.Order.StatusHistory.Add(new OrderStatusHistory
                {
                    Category = OrderHistoryCategory.Return,
                    FromStatus = ReturnRequestStatus.Approved.ToString(),
                    ToStatus = ReturnRequestStatus.AwaitingReturnShipment.ToString(),
                    Code = "RETURN_SHIPMENT_CREATE_QUEUED",
                    Title = "Đã xếp hàng tạo vận đơn hoàn trả",
                    Description = $"Return {request.Code}, shipment #{shipment.Id} chờ worker gọi GHN.",
                    ChangedBy = NormalizeActor(actor),
                    CustomerVisible = true,
                    OccurredAt = nowUtc,
                    CorrelationId = ToCorrelationId(tracker.CorrelationId)
                });

                tracker.MoveTo(CommerceFlowStage.SaveChanges);
                await _context.SaveChangesAsync(token);
                return new ShippingQueueResult(
                    outbox.Id,
                    false,
                    "Đã xếp hàng tạo vận đơn GHN chiều về.");
            },
            cancellationToken);
    }

    public async Task<ShippingOperationResult<Shipment>> SyncAsync(
        long returnRequestId,
        string actor,
        CancellationToken cancellationToken)
    {
        var snapshot = await LoadAggregateAsync(
            returnRequestId,
            tracked: false,
            cancellationToken);
        var shipment = snapshot?.Shipments
            .Where(item => item.Direction == ShipmentDirection.Return)
            .OrderByDescending(item => item.Id)
            .FirstOrDefault();

        if (snapshot is null || shipment is null)
        {
            return ShippingOperationResult<Shipment>.Failure(
                "RETURN_SHIPMENT_NOT_FOUND",
                "Return request chưa có vận đơn chiều về.");
        }

        if (string.IsNullOrWhiteSpace(shipment.ExternalOrderCode))
        {
            return ShippingOperationResult<Shipment>.Failure(
                "RETURN_TRACKING_NOT_CREATED",
                "Vận đơn chiều về chưa có mã GHN.");
        }

        var provider = await _gateway.GetDetailAsync(
            shipment.ExternalOrderCode,
            cancellationToken);
        if (!provider.Success || provider.Data is null)
        {
            return ShippingOperationResult<Shipment>.Failure(
                provider.ErrorCode ?? "RETURN_SHIPMENT_SYNC_FAILED",
                provider.Message,
                provider.Retryable);
        }

        var tracker = new CommerceFlowTracker(
            "ReturnShippingReconcile",
            nameof(ReturnRequest),
            returnRequestId.ToString(CultureInfo.InvariantCulture),
            "SyncReturnShipment",
            correlationId: Guid.NewGuid().ToString("N"));

        try
        {
            var syncedShipment = await CommerceFlowTransaction.ExecuteAsync(
                _context,
                tracker,
                async token =>
                {
                    tracker.MoveTo(CommerceFlowStage.LoadAggregate);
                    var aggregate = await LoadAggregateAsync(
                        returnRequestId,
                        tracked: true,
                        token)
                        ?? throw new BusinessRuleViolationException(
                            "RETURN_REQUEST_NOT_FOUND",
                            "Return request đã bị xóa trước khi reconcile.",
                            tracker.Snapshot());

                    var trackedShipment = aggregate.Shipments
                        .Where(item => item.Direction == ShipmentDirection.Return)
                        .OrderByDescending(item => item.Id)
                        .FirstOrDefault()
                        ?? throw new BusinessRuleViolationException(
                            "RETURN_SHIPMENT_NOT_FOUND",
                            "Return shipment đã bị xóa trước khi reconcile.",
                            tracker.Snapshot());

                    ReturnShipmentAggregateUpdater.ApplyProviderUpdate(
                        aggregate,
                        trackedShipment,
                        provider.Data,
                        actor,
                        _timeProvider.GetUtcNow().UtcDateTime,
                        tracker);

                    tracker.MoveTo(CommerceFlowStage.SaveChanges);
                    await _context.SaveChangesAsync(token);
                    return trackedShipment;
                },
                cancellationToken);

            return ShippingOperationResult<Shipment>.Ok(syncedShipment);
        }
        catch (CommerceFlowException exception)
        {
            return ShippingOperationResult<Shipment>.Failure(
                exception.ErrorCode,
                exception.Message,
                exception is CommerceConcurrencyException or CommerceTransactionException);
        }
    }

    internal static ShippingCreateRequest BuildCreateRequest(
        ReturnRequest request,
        Shipment shipment,
        GhnShippingOptions options,
        int serviceId,
        int serviceTypeId,
        string? note)
    {
        var order = request.Order;
        if (order.ShippingDistrictId is not > 0
            || string.IsNullOrWhiteSpace(order.ShippingWardCode))
        {
            throw new InvalidOperationException(
                "Địa chỉ khách gửi trả chưa có mã GHN hợp lệ.");
        }

        var approvedItems = request.Items
            .Where(item => item.ApprovedQuantity > 0)
            .OrderBy(item => item.Id)
            .ToArray();
        var totalQuantity = Math.Max(1, approvedItems.Sum(item => item.ApprovedQuantity));
        var itemWeight = Math.Max(1, shipment.WeightGram / totalQuantity);

        var items = approvedItems.Select(item => new ShippingParcelItem(
            item.OrderItem.ProductName,
            item.OrderItem.Sku,
            item.ApprovedQuantity,
            ToProviderMoney(item.OrderItem.UnitPrice),
            itemWeight,
            Math.Max(1, shipment.LengthCm),
            Math.Max(1, shipment.WidthCm),
            Math.Max(1, shipment.HeightCm))).ToArray();

        var customer = new ShippingParty(
            order.CustomerName,
            order.CustomerPhone,
            BuildCustomerAddress(order),
            order.ShippingDistrictId.Value,
            order.ShippingWardCode);
        var store = new ShippingParty(
            options.SenderName,
            options.SenderPhone,
            options.SenderAddress,
            options.FromDistrictId,
            options.FromWardCode);

        return new ShippingCreateRequest(
            $"{request.Code}-{shipment.Id}",
            customer,
            store,
            customer,
            serviceId,
            serviceTypeId,
            options.PaymentTypeId,
            options.RequiredNote,
            string.IsNullOrWhiteSpace(note)
                ? $"Hàng hoàn trả {request.Code}"
                : note.Trim(),
            shipment.WeightGram,
            shipment.LengthCm,
            shipment.WidthCm,
            shipment.HeightCm,
            Math.Min(
                ToProviderMoney(approvedItems.Sum(item =>
                    item.OrderItem.Quantity > 0
                        ? item.OrderItem.LineTotal * item.ApprovedQuantity / item.OrderItem.Quantity
                        : item.OrderItem.UnitPrice * item.ApprovedQuantity)),
                5_000_000),
            0,
            items);
    }

    private ShippingQuoteRequest BuildQuoteRequest(
        ReturnRequest request,
        ReturnShippingExecutionInput input)
    {
        var approvedItems = request.Items
            .Where(item => item.ApprovedQuantity > 0)
            .OrderBy(item => item.Id)
            .ToArray();
        var totalQuantity = Math.Max(1, approvedItems.Sum(item => item.ApprovedQuantity));
        var itemWeight = Math.Max(1, input.WeightGram / totalQuantity);
        var items = approvedItems.Select(item => new ShippingParcelItem(
            item.OrderItem.ProductName,
            item.OrderItem.Sku,
            item.ApprovedQuantity,
            ToProviderMoney(item.OrderItem.UnitPrice),
            itemWeight,
            input.LengthCm,
            input.WidthCm,
            input.HeightCm)).ToArray();

        return new ShippingQuoteRequest(
            request.Order.ShippingDistrictId!.Value,
            request.Order.ShippingWardCode!,
            _options.FromDistrictId,
            _options.FromWardCode,
            input.ServiceId,
            input.ServiceTypeId,
            input.WeightGram,
            input.LengthCm,
            input.WidthCm,
            input.HeightCm,
            Math.Min(
                ToProviderMoney(approvedItems.Sum(item =>
                    item.OrderItem.Quantity > 0
                        ? item.OrderItem.LineTotal * item.ApprovedQuantity / item.OrderItem.Quantity
                        : item.OrderItem.UnitPrice * item.ApprovedQuantity)),
                5_000_000),
            0,
            items);
    }

    private async Task<ReturnRequest?> LoadAggregateAsync(
        long returnRequestId,
        bool tracked,
        CancellationToken cancellationToken)
    {
        IQueryable<ReturnRequest> query = _context.ReturnRequests;
        if (!tracked)
        {
            query = query.AsNoTracking();
        }

        return await query
            .Include(item => item.Order)
                .ThenInclude(order => order.Shipments)
            .Include(item => item.Order)
                .ThenInclude(order => order.StatusHistory)
            .Include(item => item.Items)
                .ThenInclude(item => item.OrderItem)
            .Include(item => item.Shipments)
            .SingleOrDefaultAsync(item => item.Id == returnRequestId, cancellationToken);
    }

    private static string? ValidateInput(
        ReturnRequest request,
        ReturnShippingExecutionInput input)
    {
        if (request.Order.ShippingDistrictId is not > 0
            || string.IsNullOrWhiteSpace(request.Order.ShippingWardCode))
        {
            return "Địa chỉ khách gửi trả chưa có mã GHN hợp lệ.";
        }

        if (request.Items.All(item => item.ApprovedQuantity <= 0))
        {
            return "Return request chưa có số lượng được duyệt.";
        }

        if (input.ServiceId <= 0 || input.ServiceTypeId is not 2 and not 5)
        {
            return "Dịch vụ GHN không hợp lệ.";
        }

        if (input.WeightGram is <= 0 or > 50_000)
        {
            return "Khối lượng phải từ 1 đến 50.000 gram.";
        }

        if (input.LengthCm is <= 0 or > 200
            || input.WidthCm is <= 0 or > 200
            || input.HeightCm is <= 0 or > 200)
        {
            return "Mỗi kích thước phải từ 1 đến 200 cm.";
        }

        return null;
    }

    private void ApplyOriginalRowVersion(
        ReturnRequest request,
        byte[] rowVersion,
        CommerceFlowTracker tracker)
    {
        if (rowVersion is not { Length: > 0 })
        {
            throw new BusinessRuleViolationException(
                "RETURN_ROW_VERSION_REQUIRED",
                "RowVersion của return request là bắt buộc.",
                tracker.Snapshot());
        }

        _context.Entry(request)
            .Property(item => item.RowVersion)
            .OriginalValue = rowVersion;
    }

    private static string BuildCustomerAddress(Order order) =>
        string.Join(", ", new[]
        {
            order.ShippingAddressLine,
            order.ShippingWard,
            order.ShippingDistrict,
            order.ShippingCity
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static int ToProviderMoney(decimal value) =>
        checked((int)Math.Round(
            Math.Clamp(value, 0m, 50_000_000m),
            0,
            MidpointRounding.AwayFromZero));

    private static string NormalizeActor(string actor) =>
        string.IsNullOrWhiteSpace(actor) ? "Admin" : actor.Trim();

    private static string ToCorrelationId(string value) =>
        value.Length <= 64 ? value : value[..64];
}

public sealed record GhnCreateReturnShipmentMessage(
    long ReturnRequestId,
    long ShipmentId,
    int ServiceId,
    int ServiceTypeId,
    string? Note);
