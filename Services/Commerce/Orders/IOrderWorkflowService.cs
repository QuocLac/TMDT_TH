using WebApplication2.Models;
using WebApplication2.Models.Enums;

namespace WebApplication2.Services.Commerce.Orders;

public interface IOrderWorkflowService
{
    IReadOnlyList<OrderStatus> GetAllowedOrderTransitions(OrderStatus currentStatus);

    IReadOnlyList<PaymentStatus> GetAllowedPaymentTransitions(PaymentStatus currentStatus);

    IReadOnlyList<FulfillmentStatus> GetAllowedFulfillmentTransitions(
        FulfillmentStatus currentStatus);

    Task<Order> TransitionAsync(
        int orderId,
        OrderStatus targetStatus,
        byte[] rowVersion,
        string actor,
        string? reason,
        CancellationToken cancellationToken);

    Task<Order> TransitionPaymentAsync(
        int orderId,
        PaymentStatus targetStatus,
        byte[] rowVersion,
        string actor,
        string? reason,
        CancellationToken cancellationToken);

    Task<Order> TransitionFulfillmentAsync(
        int orderId,
        FulfillmentStatus targetStatus,
        byte[] rowVersion,
        string actor,
        string? reason,
        CancellationToken cancellationToken);
}
