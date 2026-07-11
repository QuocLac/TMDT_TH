using WebApplication2.Models;
using WebApplication2.Services.Commerce.Flows;

namespace WebApplication2.Services.Commerce.Cancellations;

public class OrderCancellationException : CommerceFlowException
{
    public OrderCancellationException(string message)
        : this(
            ResolveErrorCode(message),
            message,
            CreateFallbackContext())
    {
    }

    public OrderCancellationException(string message, Exception innerException)
        : this(
            ResolveErrorCode(message),
            message,
            CreateFallbackContext(),
            innerException)
    {
    }

    public OrderCancellationException(
        string errorCode,
        string message,
        CommerceFlowContext context,
        Exception? innerException = null)
        : base(errorCode, message, context, innerException)
    {
    }

    private static string ResolveErrorCode(string message)
    {
        var separator = message.IndexOf(':');
        if (separator > 0)
        {
            var prefix = message[..separator].Trim();
            if (prefix.All(character => char.IsUpper(character)
                || char.IsDigit(character)
                || character == '_'))
            {
                return prefix;
            }
        }

        return "CANCELLATION_RULE_VIOLATION";
    }

    private static CommerceFlowContext CreateFallbackContext() =>
        new(
            "OrderCancellation",
            CommerceFlowStage.ValidateBusinessRules,
            nameof(OrderCancellationRequest),
            "unknown",
            null,
            "CancellationOperation",
            Guid.NewGuid().ToString("N"),
            null,
            new Dictionary<string, string>());
}

public sealed class OrderCancellationConcurrencyException : OrderCancellationException
{
    public OrderCancellationConcurrencyException(string message)
        : base(
            "CANCELLATION_CONCURRENCY_CONFLICT",
            message,
            CreateFallbackContext())
    {
    }

    public OrderCancellationConcurrencyException(string message, Exception innerException)
        : base(
            "CANCELLATION_CONCURRENCY_CONFLICT",
            message,
            CreateFallbackContext(),
            innerException)
    {
    }

    public OrderCancellationConcurrencyException(
        string message,
        CommerceFlowContext context,
        Exception innerException)
        : base(
            "CANCELLATION_CONCURRENCY_CONFLICT",
            message,
            context,
            innerException)
    {
    }

    private static CommerceFlowContext CreateFallbackContext() =>
        new(
            "OrderCancellation",
            CommerceFlowStage.ValidateConcurrency,
            nameof(OrderCancellationRequest),
            "unknown",
            null,
            "CancellationOperation",
            Guid.NewGuid().ToString("N"),
            null,
            new Dictionary<string, string>());
}
