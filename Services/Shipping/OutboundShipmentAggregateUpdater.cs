using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Commerce.Flows;

namespace WebApplication2.Services.Shipping;

public static class OutboundShipmentAggregateUpdater
{
    public static ShipmentTransitionDecision ApplyProviderUpdate(
        Order order,
        Shipment shipment,
        ShippingDetail detail,
        string actor,
        DateTime nowUtc,
        CommerceFlowTracker tracker)
    {
        tracker.MoveTo(CommerceFlowStage.ApplyProviderResponse, shipment.Status.ToString())
            .AddMetadata("ProviderStatus", detail.ProviderStatus)
            .AddMetadata("ProviderOrderCode", detail.ExternalOrderCode)
            .AddMetadata("ProviderUpdatedAt", detail.ProviderUpdatedAt);

        if (shipment.Direction != ShipmentDirection.Outbound)
        {
            throw new BusinessRuleViolationException(
                "OUTBOUND_FLOW_REJECTED_RETURN_SHIPMENT",
                "Outbound shipping flow không được phép cập nhật vận đơn chiều về.",
                tracker.Snapshot());
        }

        if (detail.Status is ShipmentStatus.Picking
            or ShipmentStatus.InTransit
            or ShipmentStatus.Delivered
            or ShipmentStatus.DeliveryFailed
            or ShipmentStatus.Returning
            or ShipmentStatus.Returned)
        {
            if (order.OrderStatus is OrderStatus.Cancelled or OrderStatus.Closed)
            {
                throw new BusinessRuleViolationException(
                    "OUTBOUND_EVENT_CONFLICTS_TERMINAL_ORDER",
                    $"GHN báo {detail.Status} nhưng đơn đang ở trạng thái {order.OrderStatus}. Cần điều tra thủ công, không được tự ghi đè order lifecycle.",
                    tracker.Snapshot());
            }

            if (order.OrderStatus is not OrderStatus.Processing and not OrderStatus.Completed)
            {
                throw new BusinessRuleViolationException(
                    "OUTBOUND_PROGRESS_REQUIRES_PROCESSING_ORDER",
                    $"GHN báo {detail.Status} nhưng đơn chưa ở Processing; trạng thái hiện tại là {order.OrderStatus}.",
                    tracker.Snapshot());
            }
        }

        var decision = OutboundShipmentStateMachine.EvaluateProviderTransition(
            shipment.Status,
            detail.Status,
            shipment.ProviderUpdatedAt,
            detail.ProviderUpdatedAt,
            tracker);

        shipment.LastSyncedAt = nowUtc;

        if (decision.Stale)
        {
            shipment.UpdatedAt = nowUtc;
            return decision;
        }

        shipment.ProviderStatus = detail.ProviderStatus;
        shipment.ProviderUpdatedAt = detail.ProviderUpdatedAt
            ?? shipment.ProviderUpdatedAt
            ?? nowUtc;
        shipment.ExternalOrderCode = detail.ExternalOrderCode;
        shipment.TrackingCode = detail.ExternalOrderCode;
        shipment.Fee = detail.Fee > 0 ? detail.Fee : shipment.Fee;
        shipment.CodAmount = detail.CodAmount >= 0 ? detail.CodAmount : shipment.CodAmount;
        shipment.WeightGram = detail.WeightGram > 0 ? detail.WeightGram : shipment.WeightGram;
        shipment.LengthCm = detail.LengthCm > 0 ? detail.LengthCm : shipment.LengthCm;
        shipment.WidthCm = detail.WidthCm > 0 ? detail.WidthCm : shipment.WidthCm;
        shipment.HeightCm = detail.HeightCm > 0 ? detail.HeightCm : shipment.HeightCm;
        shipment.EstimatedDeliveryAt = detail.ExpectedDeliveryAt ?? shipment.EstimatedDeliveryAt;
        shipment.ShipperName = detail.ShipperName ?? shipment.ShipperName;
        shipment.ShipperPhone = detail.ShipperPhone ?? shipment.ShipperPhone;
        shipment.CurrentHub = detail.CurrentHub ?? shipment.CurrentHub;
        shipment.ProviderReason = detail.Reason ?? shipment.ProviderReason;

        if (!decision.Apply)
        {
            shipment.UpdatedAt = nowUtc;
            return decision;
        }

        var previousShipmentStatus = shipment.Status;
        var previousFulfillment = order.FulfillmentStatus;
        var previousOrderStatus = order.OrderStatus;
        var occurredAt = detail.ProviderUpdatedAt ?? nowUtc;

        shipment.Status = decision.TargetStatus;
        shipment.ProviderUpdatedAt = detail.ProviderUpdatedAt ?? nowUtc;
        shipment.UpdatedAt = nowUtc;

        if (shipment.Status is ShipmentStatus.Picking or ShipmentStatus.InTransit)
        {
            shipment.CarrierHandoffAt ??= occurredAt;
            RejectPendingCancellationsAfterCarrierHandoff(
                order,
                actor,
                occurredAt,
                tracker.CorrelationId);
        }

        if (shipment.Status == ShipmentStatus.Delivered)
        {
            shipment.DeliveredAt ??= occurredAt;
        }

        if (shipment.Status == ShipmentStatus.Cancelled)
        {
            shipment.CancelledAt ??= occurredAt;
        }

        if (shipment.Status != ShipmentStatus.Cancelled)
        {
            order.FulfillmentStatus = decision.TargetFulfillmentStatus;
        }
        else if (order.OrderStatus == OrderStatus.Cancelled)
        {
            order.FulfillmentStatus = FulfillmentStatus.Cancelled;
        }

        order.UpdatedAt = nowUtc;

        AddHistory(
            order,
            OrderHistoryCategory.Fulfillment,
            previousFulfillment.ToString(),
            order.FulfillmentStatus.ToString(),
            $"SHIPMENT_{shipment.Status.ToString().ToUpperInvariant()}",
            "GHN cập nhật vận chuyển",
            $"Vận đơn {detail.ExternalOrderCode}: {previousShipmentStatus} → {shipment.Status}. Provider status: {detail.ProviderStatus}.",
            actor,
            occurredAt,
            tracker.CorrelationId,
            customerVisible: true);

        if (shipment.Status == ShipmentStatus.Delivered)
        {
            MarkCodPaid(order, actor, occurredAt, tracker.CorrelationId);

            if (order.PaymentStatus == PaymentStatus.Paid)
            {
                order.OrderStatus = OrderStatus.Completed;
                order.CompletedAt ??= occurredAt;

                if (previousOrderStatus != OrderStatus.Completed)
                {
                    AddHistory(
                        order,
                        OrderHistoryCategory.Order,
                        previousOrderStatus.ToString(),
                        OrderStatus.Completed.ToString(),
                        "ORDER_COMPLETED_BY_DELIVERY",
                        "Đơn hàng đã hoàn tất",
                        "Đơn chỉ được hoàn tất sau khi GHN xác nhận giao thành công và thanh toán đã hợp lệ.",
                        actor,
                        occurredAt,
                        tracker.CorrelationId,
                        customerVisible: true);
                }
            }
        }

        return decision;
    }

    private static void RejectPendingCancellationsAfterCarrierHandoff(
        Order order,
        string actor,
        DateTime occurredAt,
        string correlationId)
    {
        var pendingRequests = order.CancellationRequests
            .Where(item => item.Status == OrderCancellationStatus.Pending)
            .OrderBy(item => item.Id)
            .ToArray();

        foreach (var request in pendingRequests)
        {
            request.Status = OrderCancellationStatus.Rejected;
            request.ReviewedAt = occurredAt;
            request.ReviewedBy = string.IsNullOrWhiteSpace(actor) ? "GHN Provider" : actor.Trim();
            request.ReviewNote =
                "CANCELLATION_AFTER_CARRIER_HANDOFF: GHN đã tiếp nhận kiện hàng; "
                + "không thể tiếp tục cancellation trước giao hàng.";
            request.UpdatedAt = occurredAt;

            AddHistory(
                order,
                OrderHistoryCategory.Order,
                OrderCancellationStatus.Pending.ToString(),
                OrderCancellationStatus.Rejected.ToString(),
                "CANCELLATION_REJECTED_AFTER_CARRIER_HANDOFF",
                "Yêu cầu hủy bị từ chối sau khi GHN tiếp nhận",
                $"Cancellation request #{request.Id} bị khóa vì vận đơn đã được bàn giao cho GHN. "
                + "Sau khi giao thành công, khách hàng có thể sử dụng return workflow trong thời hạn cho phép.",
                actor,
                occurredAt,
                correlationId,
                customerVisible: true);
        }
    }

    private static void MarkCodPaid(
        Order order,
        string actor,
        DateTime occurredAt,
        string correlationId)
    {
        if (order.PaymentStatus != PaymentStatus.CodPending)
        {
            return;
        }

        var payment = order.PaymentTransactions
            .Where(item => item.Status == PaymentStatus.CodPending)
            .OrderByDescending(item => item.AttemptNumber)
            .ThenByDescending(item => item.Id)
            .FirstOrDefault();

        if (payment is null
            || !string.Equals(payment.Method, "COD", StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleViolationException(
                "COD_TRANSACTION_NOT_FOUND",
                "Order đang ở CodPending nhưng không tìm thấy payment transaction COD tương ứng.",
                new CommerceFlowContext(
                    "OutboundShipment",
                    CommerceFlowStage.UpdateRelatedAggregate,
                    nameof(Order),
                    order.Id.ToString(),
                    order.PaymentStatus.ToString(),
                    "MarkCodPaid",
                    correlationId,
                    null,
                    new Dictionary<string, string>()));
        }

        order.PaymentStatus = PaymentStatus.Paid;
        payment.Status = PaymentStatus.Paid;
        payment.CompletedAt ??= occurredAt;
        payment.UpdatedAt = occurredAt;

        AddHistory(
            order,
            OrderHistoryCategory.Payment,
            PaymentStatus.CodPending.ToString(),
            PaymentStatus.Paid.ToString(),
            "COD_COLLECTED_BY_GHN",
            "GHN xác nhận đã thu COD",
            "Thanh toán COD được ghi nhận từ sự kiện giao hàng thành công của GHN.",
            actor,
            occurredAt,
            correlationId,
            customerVisible: true);
    }

    private static void AddHistory(
        Order order,
        OrderHistoryCategory category,
        string? fromStatus,
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
            Category = category,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            Code = code,
            Title = title,
            Description = description,
            ChangedBy = string.IsNullOrWhiteSpace(actor) ? "System" : actor.Trim(),
            CustomerVisible = customerVisible,
            OccurredAt = occurredAt,
            CorrelationId = correlationId.Length <= 64
                ? correlationId
                : correlationId[..64]
        });
    }
}
