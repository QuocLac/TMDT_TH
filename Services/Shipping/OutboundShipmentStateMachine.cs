using WebApplication2.Models.Enums;
using WebApplication2.Services.Commerce.Flows;

namespace WebApplication2.Services.Shipping;

public enum ShipmentTransitionOrigin
{
    Internal,
    OutboxWorker,
    ProviderWebhook,
    ProviderReconciliation
}

public sealed record ShipmentTransitionDecision(
    bool Apply,
    bool Duplicate,
    bool Stale,
    ShipmentStatus TargetStatus,
    FulfillmentStatus TargetFulfillmentStatus,
    string? IgnoreReason);

public static class OutboundShipmentStateMachine
{
    private static readonly IReadOnlyDictionary<ShipmentStatus, ShipmentStatus[]> InternalTransitions =
        new Dictionary<ShipmentStatus, ShipmentStatus[]>
        {
            [ShipmentStatus.Draft] = [ShipmentStatus.PendingCreation, ShipmentStatus.Cancelled],
            [ShipmentStatus.PendingCreation] = [ShipmentStatus.Created, ShipmentStatus.CancelRequested, ShipmentStatus.Cancelled, ShipmentStatus.Exception],
            [ShipmentStatus.Created] = [ShipmentStatus.CancelRequested, ShipmentStatus.Exception],
            [ShipmentStatus.CancelRequested] = [ShipmentStatus.Cancelled, ShipmentStatus.Exception],
            [ShipmentStatus.Picking] = [ShipmentStatus.Exception],
            [ShipmentStatus.InTransit] = [ShipmentStatus.Exception],
            [ShipmentStatus.Delivered] = [],
            [ShipmentStatus.DeliveryFailed] = [ShipmentStatus.Exception],
            [ShipmentStatus.Returning] = [ShipmentStatus.Exception],
            [ShipmentStatus.Returned] = [],
            [ShipmentStatus.Cancelled] = [],
            [ShipmentStatus.Exception] = [ShipmentStatus.PendingCreation, ShipmentStatus.CancelRequested]
        };

    private static readonly IReadOnlyDictionary<ShipmentStatus, ShipmentStatus[]> ProviderTransitions =
        new Dictionary<ShipmentStatus, ShipmentStatus[]>
        {
            [ShipmentStatus.Draft] = [],
            [ShipmentStatus.PendingCreation] =
            [
                ShipmentStatus.Created,
                ShipmentStatus.Cancelled,
                ShipmentStatus.Exception
            ],
            [ShipmentStatus.Created] =
            [
                ShipmentStatus.Picking,
                ShipmentStatus.InTransit,
                ShipmentStatus.Cancelled,
                ShipmentStatus.Exception
            ],
            [ShipmentStatus.CancelRequested] =
            [
                ShipmentStatus.Cancelled,
                ShipmentStatus.Picking,
                ShipmentStatus.InTransit,
                ShipmentStatus.Exception
            ],
            [ShipmentStatus.Picking] =
            [
                ShipmentStatus.InTransit,
                ShipmentStatus.DeliveryFailed,
                ShipmentStatus.Returning,
                ShipmentStatus.Exception
            ],
            [ShipmentStatus.InTransit] =
            [
                ShipmentStatus.Delivered,
                ShipmentStatus.DeliveryFailed,
                ShipmentStatus.Returning,
                ShipmentStatus.Exception
            ],
            [ShipmentStatus.DeliveryFailed] =
            [
                ShipmentStatus.InTransit,
                ShipmentStatus.Returning,
                ShipmentStatus.Returned,
                ShipmentStatus.Exception
            ],
            [ShipmentStatus.Returning] =
            [
                ShipmentStatus.Returned,
                ShipmentStatus.Exception
            ],
            [ShipmentStatus.Delivered] = [],
            [ShipmentStatus.Returned] = [],
            [ShipmentStatus.Cancelled] = [],
            [ShipmentStatus.Exception] =
            [
                ShipmentStatus.Picking,
                ShipmentStatus.InTransit,
                ShipmentStatus.DeliveryFailed,
                ShipmentStatus.Returning,
                ShipmentStatus.Returned,
                ShipmentStatus.Cancelled
            ]
        };

    public static void EnsureInternalTransition(
        ShipmentStatus current,
        ShipmentStatus target,
        CommerceFlowTracker tracker)
    {
        tracker.MoveTo(CommerceFlowStage.ValidateStateTransition, current.ToString())
            .AddMetadata("TargetShipmentStatus", target);

        if (!InternalTransitions.TryGetValue(current, out var allowed)
            || !allowed.Contains(target))
        {
            throw new InvalidStateTransitionException(
                "SHIPMENT_INTERNAL_TRANSITION_NOT_ALLOWED",
                $"Không thể chuyển shipment nội bộ từ {current} sang {target}.",
                tracker.Snapshot());
        }
    }

    public static ShipmentTransitionDecision EvaluateProviderTransition(
        ShipmentStatus current,
        ShipmentStatus incoming,
        DateTime? currentProviderUpdatedAt,
        DateTime? incomingProviderUpdatedAt,
        CommerceFlowTracker tracker)
    {
        tracker.MoveTo(CommerceFlowStage.ValidateStateTransition, current.ToString())
            .AddMetadata("IncomingShipmentStatus", incoming)
            .AddMetadata("CurrentProviderUpdatedAt", currentProviderUpdatedAt)
            .AddMetadata("IncomingProviderUpdatedAt", incomingProviderUpdatedAt);

        if (currentProviderUpdatedAt.HasValue
            && incomingProviderUpdatedAt.HasValue
            && incomingProviderUpdatedAt.Value < currentProviderUpdatedAt.Value)
        {
            return new ShipmentTransitionDecision(
                Apply: false,
                Duplicate: false,
                Stale: true,
                incoming,
                MapFulfillment(incoming),
                "Provider event cũ hơn event đã áp dụng.");
        }

        if (incoming == current)
        {
            return new ShipmentTransitionDecision(
                Apply: false,
                Duplicate: true,
                Stale: false,
                incoming,
                MapFulfillment(incoming),
                "Provider status trùng trạng thái hiện tại; chỉ refresh provider snapshot.");
        }

        if (!ProviderTransitions.TryGetValue(current, out var allowed)
            || !allowed.Contains(incoming))
        {
            throw new ProviderEventOrderException(
                "SHIPMENT_PROVIDER_EVENT_OUT_OF_ORDER",
                $"Provider event yêu cầu chuyển shipment từ {current} sang {incoming}, nhưng transition này không hợp lệ.",
                tracker.Snapshot());
        }

        return new ShipmentTransitionDecision(
            Apply: true,
            Duplicate: false,
            Stale: false,
            incoming,
            MapFulfillment(incoming),
            null);
    }

    public static void EnsureCancellationCanBeRequested(
        ShipmentStatus current,
        DateTime? carrierHandoffAt,
        CommerceFlowTracker tracker)
    {
        tracker.MoveTo(CommerceFlowStage.ValidateBusinessRules, current.ToString());

        if (carrierHandoffAt.HasValue
            || current is ShipmentStatus.Picking
                or ShipmentStatus.InTransit
                or ShipmentStatus.Delivered
                or ShipmentStatus.DeliveryFailed
                or ShipmentStatus.Returning
                or ShipmentStatus.Returned)
        {
            throw new BusinessRuleViolationException(
                "CANCELLATION_AFTER_CARRIER_HANDOFF",
                "Vận đơn đã được GHN tiếp nhận hoặc đã phát sinh giao hàng. Không thể hủy đơn theo luồng trước giao hàng; khách hàng phải dùng quy trình hoàn trả sau khi giao thành công.",
                tracker.Snapshot());
        }

        if (current is ShipmentStatus.Cancelled)
        {
            throw new BusinessRuleViolationException(
                "SHIPMENT_ALREADY_CANCELLED",
                "Vận đơn đã được hủy trước đó.",
                tracker.Snapshot());
        }
    }

    public static FulfillmentStatus MapFulfillment(ShipmentStatus status) => status switch
    {
        ShipmentStatus.Draft or ShipmentStatus.PendingCreation => FulfillmentStatus.Preparing,
        ShipmentStatus.Created or ShipmentStatus.CancelRequested => FulfillmentStatus.ReadyToShip,
        ShipmentStatus.Picking or ShipmentStatus.InTransit => FulfillmentStatus.Shipped,
        ShipmentStatus.Delivered => FulfillmentStatus.Delivered,
        ShipmentStatus.DeliveryFailed or ShipmentStatus.Exception => FulfillmentStatus.DeliveryFailed,
        ShipmentStatus.Returning => FulfillmentStatus.Returning,
        ShipmentStatus.Returned => FulfillmentStatus.Returned,
        ShipmentStatus.Cancelled => FulfillmentStatus.Cancelled,
        _ => FulfillmentStatus.Unfulfilled
    };
}
