using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Commerce.Flows;

namespace WebApplication2.Services.Shipping;

public static class ReturnShipmentAggregateUpdater
{
    public static ShipmentTransitionDecision ApplyProviderUpdate(
        ReturnRequest request,
        Shipment shipment,
        ShippingDetail detail,
        string actor,
        DateTime nowUtc,
        CommerceFlowTracker tracker)
    {
        tracker.MoveTo(CommerceFlowStage.ApplyProviderResponse, shipment.Status.ToString())
            .AddMetadata("ReturnRequestId", request.Id)
            .AddMetadata("ProviderStatus", detail.ProviderStatus)
            .AddMetadata("ProviderUpdatedAt", detail.ProviderUpdatedAt);

        if (shipment.Direction != ShipmentDirection.Return
            || shipment.ReturnRequestId != request.Id)
        {
            throw new BusinessRuleViolationException(
                "RETURN_SHIPMENT_OWNERSHIP_INVALID",
                "Return shipping flow nhận shipment không thuộc return request.",
                tracker.Snapshot());
        }

        if (shipment.ProviderUpdatedAt.HasValue
            && detail.ProviderUpdatedAt.HasValue
            && detail.ProviderUpdatedAt.Value < shipment.ProviderUpdatedAt.Value)
        {
            shipment.LastSyncedAt = nowUtc;
            shipment.UpdatedAt = nowUtc;
            return new ShipmentTransitionDecision(
                false,
                false,
                true,
                detail.Status,
                request.Order.FulfillmentStatus,
                "Provider event cũ hơn snapshot đã áp dụng.");
        }

        shipment.LastSyncedAt = nowUtc;
        shipment.ProviderStatus = detail.ProviderStatus;
        shipment.ProviderUpdatedAt = detail.ProviderUpdatedAt
            ?? shipment.ProviderUpdatedAt
            ?? nowUtc;
        shipment.ExternalOrderCode = detail.ExternalOrderCode;
        shipment.TrackingCode = detail.ExternalOrderCode;
        shipment.Fee = detail.Fee > 0 ? detail.Fee : shipment.Fee;
        shipment.CodAmount = 0;
        shipment.WeightGram = detail.WeightGram > 0 ? detail.WeightGram : shipment.WeightGram;
        shipment.LengthCm = detail.LengthCm > 0 ? detail.LengthCm : shipment.LengthCm;
        shipment.WidthCm = detail.WidthCm > 0 ? detail.WidthCm : shipment.WidthCm;
        shipment.HeightCm = detail.HeightCm > 0 ? detail.HeightCm : shipment.HeightCm;
        shipment.EstimatedDeliveryAt = detail.ExpectedDeliveryAt ?? shipment.EstimatedDeliveryAt;
        shipment.ShipperName = detail.ShipperName ?? shipment.ShipperName;
        shipment.ShipperPhone = detail.ShipperPhone ?? shipment.ShipperPhone;
        shipment.CurrentHub = detail.CurrentHub ?? shipment.CurrentHub;
        shipment.ProviderReason = detail.Reason ?? shipment.ProviderReason;

        if (shipment.Status == detail.Status)
        {
            shipment.UpdatedAt = nowUtc;
            return new ShipmentTransitionDecision(
                false,
                true,
                false,
                detail.Status,
                request.Order.FulfillmentStatus,
                "Provider status trùng trạng thái hiện tại.");
        }

        EnsureTransition(shipment.Status, detail.Status, tracker);

        var previousShipment = shipment.Status;
        var previousReturn = request.Status;
        var occurredAt = detail.ProviderUpdatedAt ?? nowUtc;

        shipment.Status = detail.Status;
        shipment.UpdatedAt = nowUtc;

        switch (detail.Status)
        {
            case ShipmentStatus.Created:
                request.Status = ReturnRequestStatus.AwaitingPickup;
                break;

            case ShipmentStatus.Picking:
            case ShipmentStatus.InTransit:
                shipment.CarrierHandoffAt ??= occurredAt;
                request.Status = ReturnRequestStatus.ReturnInTransit;
                break;

            case ShipmentStatus.Delivered:
                shipment.DeliveredAt ??= occurredAt;
                request.Status = ReturnRequestStatus.ReceivedAtWarehouse;
                request.ReceivedAt ??= occurredAt;
                break;

            case ShipmentStatus.Cancelled:
                shipment.CancelledAt ??= occurredAt;
                request.Status = ReturnRequestStatus.AwaitingReturnShipment;
                break;

            case ShipmentStatus.Returned:
                request.Status = ReturnRequestStatus.AwaitingReturnShipment;
                break;
        }

        request.UpdatedAt = nowUtc;

        request.Order.StatusHistory.Add(new OrderStatusHistory
        {
            Category = OrderHistoryCategory.Return,
            FromStatus = previousReturn.ToString(),
            ToStatus = request.Status.ToString(),
            Code = $"RETURN_SHIPMENT_{detail.Status.ToString().ToUpperInvariant()}",
            Title = "GHN cập nhật vận đơn hoàn trả",
            Description =
                $"Return {request.Code}, shipment #{shipment.Id}: "
                + $"{previousShipment} → {shipment.Status}; provider={detail.ProviderStatus}.",
            ChangedBy = string.IsNullOrWhiteSpace(actor) ? "GHN Provider" : actor.Trim(),
            CustomerVisible = true,
            OccurredAt = occurredAt,
            CorrelationId = ToCorrelationId(tracker.CorrelationId)
        });

        return new ShipmentTransitionDecision(
            true,
            false,
            false,
            detail.Status,
            request.Order.FulfillmentStatus,
            null);
    }

    private static void EnsureTransition(
        ShipmentStatus current,
        ShipmentStatus incoming,
        CommerceFlowTracker tracker)
    {
        var allowed = current switch
        {
            ShipmentStatus.PendingCreation =>
                incoming is ShipmentStatus.Created
                    or ShipmentStatus.Cancelled
                    or ShipmentStatus.Exception,
            ShipmentStatus.Created =>
                incoming is ShipmentStatus.Picking
                    or ShipmentStatus.InTransit
                    or ShipmentStatus.Cancelled
                    or ShipmentStatus.Exception,
            ShipmentStatus.Picking =>
                incoming is ShipmentStatus.InTransit
                    or ShipmentStatus.DeliveryFailed
                    or ShipmentStatus.Exception,
            ShipmentStatus.InTransit =>
                incoming is ShipmentStatus.Delivered
                    or ShipmentStatus.DeliveryFailed
                    or ShipmentStatus.Exception,
            ShipmentStatus.DeliveryFailed =>
                incoming is ShipmentStatus.InTransit
                    or ShipmentStatus.Returning
                    or ShipmentStatus.Returned
                    or ShipmentStatus.Exception,
            ShipmentStatus.Returning =>
                incoming is ShipmentStatus.Returned or ShipmentStatus.Exception,
            ShipmentStatus.Exception =>
                incoming is ShipmentStatus.Created
                    or ShipmentStatus.Picking
                    or ShipmentStatus.InTransit
                    or ShipmentStatus.DeliveryFailed
                    or ShipmentStatus.Delivered
                    or ShipmentStatus.Returned
                    or ShipmentStatus.Cancelled,
            _ => false
        };

        if (!allowed)
        {
            throw new ProviderEventOrderException(
                "RETURN_SHIPMENT_PROVIDER_EVENT_OUT_OF_ORDER",
                $"Không thể chuyển return shipment từ {current} sang {incoming}.",
                tracker.MoveTo(
                    CommerceFlowStage.ValidateStateTransition,
                    current.ToString()).Snapshot());
        }
    }

    private static string ToCorrelationId(string value) =>
        value.Length <= 64 ? value : value[..64];
}
