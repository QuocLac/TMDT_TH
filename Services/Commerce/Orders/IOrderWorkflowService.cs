using WebApplication2.Models;
using WebApplication2.Models.Enums;

namespace WebApplication2.Services.Commerce.Orders;

public interface IOrderWorkflowService
{
    Task<Order> TransitionAsync(
        int orderId,
        OrderStatus targetStatus,
        byte[] rowVersion,
        string actor,
        string? reason,
        CancellationToken cancellationToken);
}
