using WebApplication2.Models;
using WebApplication2.Services.Commerce.Flows;

namespace WebApplication2.Services.Commerce.Orders;

public sealed class OrderTransitionException : CommerceFlowException
{
    public OrderTransitionException(string message)
        : base(
            "ORDER_TRANSITION_REJECTED",
            message,
            CreateFallbackContext())
    {
    }

    public OrderTransitionException(
        string errorCode,
        string message,
        CommerceFlowContext context,
        Exception? innerException = null)
        : base(errorCode, message, context, innerException)
    {
    }

    private static CommerceFlowContext CreateFallbackContext() =>
        new(
            "OrderWorkflow",
            CommerceFlowStage.ValidateStateTransition,
            nameof(Order),
            "unknown",
            null,
            "Transition",
            Guid.NewGuid().ToString("N"),
            null,
            new Dictionary<string, string>());
}

public sealed class OrderConcurrencyException : CommerceFlowException
{
    public OrderConcurrencyException(string message, Exception innerException)
        : base(
            "ORDER_CONCURRENCY_CONFLICT",
            message,
            CreateFallbackContext(),
            innerException)
    {
    }

    public OrderConcurrencyException(
        string message,
        CommerceFlowContext context,
        Exception innerException)
        : base(
            "ORDER_CONCURRENCY_CONFLICT",
            message,
            context,
            innerException)
    {
    }

    private static CommerceFlowContext CreateFallbackContext() =>
        new(
            "OrderWorkflow",
            CommerceFlowStage.ValidateConcurrency,
            nameof(Order),
            "unknown",
            null,
            "Transition",
            Guid.NewGuid().ToString("N"),
            null,
            new Dictionary<string, string>());
}
