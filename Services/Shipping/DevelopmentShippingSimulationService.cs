using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Commerce.Flows;

namespace WebApplication2.Services.Shipping;

public static class DevelopmentShippingSimulationService
{
    private const string SimulatorActor = "DevelopmentShippingSimulator";

    private static readonly IReadOnlySet<ShipmentStatus> AllowedTargets =
        new HashSet<ShipmentStatus>
        {
            ShipmentStatus.Picking,
            ShipmentStatus.InTransit,
            ShipmentStatus.Delivered,
            ShipmentStatus.DeliveryFailed,
            ShipmentStatus.Returning,
            ShipmentStatus.Returned
        };

    public static Task<DevelopmentShippingSimulationResult> SimulateAsync(
        ApplicationDbContext context,
        long shipmentId,
        ShipmentStatus targetStatus,
        string actor,
        bool isDevelopment,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!isDevelopment)
        {
            return Task.FromResult(
                DevelopmentShippingSimulationResult.Failure(
                    "DEV_SHIPPING_SIMULATOR_DISABLED",
                    "Công cụ mô phỏng giao vận chỉ được phép chạy trong môi trường Development."));
        }

        if (shipmentId <= 0)
        {
            return Task.FromResult(
                DevelopmentShippingSimulationResult.Failure(
                    "DEV_SHIPPING_ID_INVALID",
                    "Mã vận đơn nội bộ không hợp lệ."));
        }

        if (!AllowedTargets.Contains(targetStatus))
        {
            return Task.FromResult(
                DevelopmentShippingSimulationResult.Failure(
                    "DEV_SHIPPING_TARGET_INVALID",
                    "Chỉ được mô phỏng các trạng thái phát sinh từ đơn vị vận chuyển."));
        }

        var tracker = new CommerceFlowTracker(
            "DevelopmentOutboundShippingSimulation",
            nameof(Shipment),
            shipmentId.ToString(CultureInfo.InvariantCulture),
            $"Simulate{targetStatus}",
            correlationId: $"dev-shipping:{shipmentId}:{Guid.NewGuid():N}")
            .AddMetadata("TargetShipmentStatus", targetStatus)
            .AddMetadata("Simulation", true);

        return CommerceFlowTransaction.ExecuteAsync(
            context,
            tracker,
            async token =>
            {
                tracker.MoveTo(CommerceFlowStage.LoadAggregate);

                var shipment = await context.Shipments
                    .Include(item => item.Order)
                        .ThenInclude(order => order.PaymentTransactions)
                    .Include(item => item.Order)
                        .ThenInclude(order => order.StatusHistory)
                    .Include(item => item.Order)
                        .ThenInclude(order => order.CancellationRequests)
                    .SingleOrDefaultAsync(
                        item => item.Id == shipmentId,
                        token)
                    ?? throw new BusinessRuleViolationException(
                        "DEV_SHIPPING_SHIPMENT_NOT_FOUND",
                        "Không tìm thấy vận đơn cần mô phỏng.",
                        tracker.Snapshot());

                if (shipment.Direction != ShipmentDirection.Outbound)
                {
                    throw new BusinessRuleViolationException(
                        "DEV_SHIPPING_OUTBOUND_REQUIRED",
                        "Công cụ này chỉ mô phỏng vận đơn chiều giao tới khách hàng.",
                        tracker.Snapshot());
                }

                if (string.IsNullOrWhiteSpace(shipment.ExternalOrderCode))
                {
                    throw new BusinessRuleViolationException(
                        "DEV_SHIPPING_PROVIDER_REFERENCE_REQUIRED",
                        "Vận đơn phải được GHN Sandbox tạo thành công và có mã vận đơn trước khi mô phỏng tiến trình.",
                        tracker.Snapshot());
                }

                if (shipment.Status == targetStatus)
                {
                    return DevelopmentShippingSimulationResult.Completed(
                        "DEV_SHIPPING_ALREADY_APPLIED",
                        $"Vận đơn đã ở trạng thái {targetStatus}.",
                        shipment.Status,
                        alreadyProcessed: true);
                }

                var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
                var providerOccurredAt =
                    shipment.ProviderUpdatedAt.HasValue
                    && shipment.ProviderUpdatedAt.Value >= nowUtc
                        ? shipment.ProviderUpdatedAt.Value.AddMilliseconds(1)
                        : nowUtc;

                var providerStatus = ToProviderStatus(targetStatus);
                var normalizedActor = NormalizeActor(actor);

                var payload = JsonSerializer.Serialize(new
                {
                    simulated = true,
                    source = SimulatorActor,
                    shipmentId = shipment.Id,
                    orderId = shipment.OrderId,
                    externalOrderCode = shipment.ExternalOrderCode,
                    previousStatus = shipment.Status.ToString(),
                    targetStatus = targetStatus.ToString(),
                    providerStatus,
                    simulatedBy = normalizedActor,
                    occurredAtUtc = providerOccurredAt
                });

                var detail = new ShippingDetail(
                    shipment.ExternalOrderCode,
                    providerStatus,
                    targetStatus,
                    shipment.Fee,
                    shipment.CodAmount,
                    shipment.WeightGram,
                    shipment.LengthCm,
                    shipment.WidthCm,
                    shipment.HeightCm,
                    shipment.EstimatedDeliveryAt,
                    providerOccurredAt,
                    shipment.ShipperName ?? "GHN Sandbox Simulator",
                    shipment.ShipperPhone,
                    ResolveCurrentHub(targetStatus, shipment.CurrentHub),
                    ResolveReason(targetStatus),
                    payload);

                tracker.MoveTo(
                    CommerceFlowStage.ApplyProviderResponse,
                    shipment.Status.ToString());

                var decision =
                    OutboundShipmentAggregateUpdater.ApplyProviderUpdate(
                        shipment.Order,
                        shipment,
                        detail,
                        SimulatorActor,
                        nowUtc,
                        tracker);

                if (!decision.Apply)
                {
                    return DevelopmentShippingSimulationResult.Completed(
                        decision.Duplicate
                            ? "DEV_SHIPPING_DUPLICATE"
                            : "DEV_SHIPPING_IGNORED",
                        decision.IgnoreReason
                            ?? "Trạng thái mô phỏng không tạo ra thay đổi.",
                        shipment.Status,
                        alreadyProcessed: true);
                }

                tracker.MoveTo(CommerceFlowStage.WriteInbox);

                var eventId =
                    $"DEV-{shipment.Id}-{targetStatus}-"
                    + $"{providerOccurredAt:yyyyMMddHHmmssfff}";
                var deduplicationKey =
                    $"dev-shipping:{shipment.Id}:{targetStatus}:"
                    + $"{providerOccurredAt:O}";

                context.IntegrationInboxEvents.Add(
                    new IntegrationInboxEvent
                    {
                        Provider = "GHN",
                        EventType = "DevelopmentShipmentStatusSimulated",
                        ExternalEventId = Truncate(eventId, 150),
                        DeduplicationKey =
                            Truncate(deduplicationKey, 160),
                        Status = IntegrationEventStatus.Processed,
                        Payload = payload,
                        Headers = JsonSerializer.Serialize(new
                        {
                            simulated = true,
                            environment = "Development"
                        }),
                        ProviderOccurredAt = providerOccurredAt,
                        ReceivedAt = nowUtc,
                        ProcessedAt = nowUtc,
                        AttemptCount = 1,
                        CorrelationId =
                            Truncate(shipment.ExternalOrderCode, 64),
                        CreatedAt = nowUtc,
                        UpdatedAt = nowUtc
                    });

                tracker.MoveTo(CommerceFlowStage.SaveChanges);
                await context.SaveChangesAsync(token);

                return DevelopmentShippingSimulationResult.Completed(
                    "DEV_SHIPPING_STATUS_APPLIED",
                    BuildSuccessMessage(
                        shipment.Order.Code,
                        targetStatus),
                    shipment.Status);
            },
            cancellationToken);
    }

    private static string ToProviderStatus(
        ShipmentStatus status) => status switch
    {
        ShipmentStatus.Picking => "picking",
        ShipmentStatus.InTransit => "delivering",
        ShipmentStatus.Delivered => "delivered",
        ShipmentStatus.DeliveryFailed => "delivery_fail",
        ShipmentStatus.Returning => "return",
        ShipmentStatus.Returned => "returned",
        _ => throw new ArgumentOutOfRangeException(
            nameof(status),
            status,
            "Unsupported simulated provider status.")
    };

    private static string? ResolveCurrentHub(
        ShipmentStatus status,
        string? existingHub) => status switch
    {
        ShipmentStatus.Picking => "Điểm lấy hàng mô phỏng",
        ShipmentStatus.InTransit => "Trung tâm trung chuyển mô phỏng",
        ShipmentStatus.Delivered => "Đã giao tới người nhận",
        ShipmentStatus.DeliveryFailed =>
            existingHub ?? "Điểm giao hàng mô phỏng",
        ShipmentStatus.Returning =>
            "Trung tâm hoàn hàng mô phỏng",
        ShipmentStatus.Returned =>
            "Kho hoàn hàng mô phỏng",
        _ => existingHub
    };

    private static string? ResolveReason(
        ShipmentStatus status) => status switch
    {
        ShipmentStatus.DeliveryFailed =>
            "Mô phỏng giao hàng chưa thành công trong môi trường Development.",
        ShipmentStatus.Returning =>
            "Mô phỏng kiện hàng đang được hoàn về.",
        ShipmentStatus.Returned =>
            "Mô phỏng kiện hàng đã hoàn về kho.",
        _ => null
    };

    private static string BuildSuccessMessage(
        string orderCode,
        ShipmentStatus status) => status switch
    {
        ShipmentStatus.Picking =>
            $"Đã mô phỏng GHN tiếp nhận kiện hàng của đơn {orderCode}.",
        ShipmentStatus.InTransit =>
            $"Đã mô phỏng đơn {orderCode} chuyển sang đang giao.",
        ShipmentStatus.Delivered =>
            $"Đã mô phỏng giao thành công đơn {orderCode}. Trạng thái đơn, fulfillment và COD đã được xử lý theo nghiệp vụ hiện có.",
        ShipmentStatus.DeliveryFailed =>
            $"Đã mô phỏng giao chưa thành công đơn {orderCode}.",
        ShipmentStatus.Returning =>
            $"Đã mô phỏng đơn {orderCode} đang hoàn về.",
        ShipmentStatus.Returned =>
            $"Đã mô phỏng đơn {orderCode} đã hoàn về kho.",
        _ =>
            $"Đã mô phỏng trạng thái {status} cho đơn {orderCode}."
    };

    private static string NormalizeActor(string? actor)
    {
        var value = string.IsNullOrWhiteSpace(actor)
            ? "Development Admin"
            : actor.Trim();

        return value.Length <= 150
            ? value
            : value[..150];
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength
            ? value
            : value[..maxLength];
    }
}

public sealed record DevelopmentShippingSimulationResult(
    bool Success,
    string Code,
    string Message,
    ShipmentStatus? Status,
    bool AlreadyProcessed)
{
    public static DevelopmentShippingSimulationResult Completed(
        string code,
        string message,
        ShipmentStatus status,
        bool alreadyProcessed = false) =>
        new(
            true,
            code,
            message,
            status,
            alreadyProcessed);

    public static DevelopmentShippingSimulationResult Failure(
        string code,
        string message) =>
        new(
            false,
            code,
            message,
            null,
            false);
}
