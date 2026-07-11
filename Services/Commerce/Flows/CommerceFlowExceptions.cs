using System.Text;

namespace WebApplication2.Services.Commerce.Flows;

public abstract class CommerceFlowException : InvalidOperationException
{
    protected CommerceFlowException(
        string errorCode,
        string message,
        CommerceFlowContext context,
        Exception? innerException = null)
        : base(BuildMessage(errorCode, message, context), innerException)
    {
        ErrorCode = Require(errorCode, nameof(errorCode));
        DetailMessage = string.IsNullOrWhiteSpace(message)
            ? "Commerce flow failed."
            : message.Trim();
        FlowContext = context ?? throw new ArgumentNullException(nameof(context));

        Data[nameof(ErrorCode)] = ErrorCode;
        Data[nameof(DetailMessage)] = DetailMessage;
        Data[nameof(FlowContext.FlowName)] = FlowContext.FlowName;
        Data[nameof(FlowContext.Stage)] = FlowContext.Stage.ToString();
        Data[nameof(FlowContext.AggregateType)] = FlowContext.AggregateType;
        Data[nameof(FlowContext.AggregateId)] = FlowContext.AggregateId;
        Data[nameof(FlowContext.CurrentState)] = FlowContext.CurrentState ?? string.Empty;
        Data[nameof(FlowContext.RequestedAction)] = FlowContext.RequestedAction ?? string.Empty;
        Data[nameof(FlowContext.CorrelationId)] = FlowContext.CorrelationId;
        Data[nameof(FlowContext.IdempotencyKey)] = FlowContext.IdempotencyKey ?? string.Empty;

        foreach (var pair in FlowContext.Metadata)
        {
            Data[$"Metadata:{pair.Key}"] = pair.Value;
        }
    }

    public string ErrorCode { get; }
    public string DetailMessage { get; }
    public CommerceFlowContext FlowContext { get; }

    private static string BuildMessage(
        string errorCode,
        string message,
        CommerceFlowContext context)
    {
        var builder = new StringBuilder();
        builder.Append('[').Append(errorCode).Append("] ")
            .Append(message.Trim())
            .Append(" | Flow=").Append(context.FlowName)
            .Append(" | Stage=").Append(context.Stage)
            .Append(" | Aggregate=").Append(context.AggregateType)
            .Append(':').Append(context.AggregateId)
            .Append(" | Action=").Append(context.RequestedAction)
            .Append(" | CorrelationId=").Append(context.CorrelationId);

        if (!string.IsNullOrWhiteSpace(context.CurrentState))
        {
            builder.Append(" | CurrentState=").Append(context.CurrentState);
        }

        if (!string.IsNullOrWhiteSpace(context.IdempotencyKey))
        {
            builder.Append(" | IdempotencyKey=").Append(context.IdempotencyKey);
        }

        return builder.ToString();
    }

    private static string Require(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }

        return value.Trim();
    }
}

public sealed class BusinessRuleViolationException : CommerceFlowException
{
    public BusinessRuleViolationException(
        string errorCode,
        string message,
        CommerceFlowContext context,
        Exception? innerException = null)
        : base(errorCode, message, context, innerException)
    {
    }
}

public sealed class InvalidStateTransitionException : CommerceFlowException
{
    public InvalidStateTransitionException(
        string errorCode,
        string message,
        CommerceFlowContext context,
        Exception? innerException = null)
        : base(errorCode, message, context, innerException)
    {
    }
}

public sealed class CommerceConcurrencyException : CommerceFlowException
{
    public CommerceConcurrencyException(
        string errorCode,
        string message,
        CommerceFlowContext context,
        Exception innerException)
        : base(errorCode, message, context, innerException)
    {
    }
}

public sealed class ProviderOperationException : CommerceFlowException
{
    public ProviderOperationException(
        string errorCode,
        string message,
        CommerceFlowContext context,
        bool retryable,
        Exception? innerException = null)
        : base(errorCode, message, context, innerException)
    {
        Retryable = retryable;
        Data[nameof(Retryable)] = retryable;
    }

    public bool Retryable { get; }
}

public sealed class ProviderEventOrderException : CommerceFlowException
{
    public ProviderEventOrderException(
        string errorCode,
        string message,
        CommerceFlowContext context)
        : base(errorCode, message, context)
    {
    }
}

public sealed class CommerceTransactionException : CommerceFlowException
{
    public CommerceTransactionException(
        string errorCode,
        string message,
        CommerceFlowContext context,
        Exception innerException)
        : base(errorCode, message, context, innerException)
    {
    }
}

public sealed class IdempotencyConflictException : CommerceFlowException
{
    public IdempotencyConflictException(
        string errorCode,
        string message,
        CommerceFlowContext context,
        Exception? innerException = null)
        : base(errorCode, message, context, innerException)
    {
    }
}
