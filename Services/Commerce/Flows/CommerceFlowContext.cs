namespace WebApplication2.Services.Commerce.Flows;

public sealed record CommerceFlowContext(
    string FlowName,
    CommerceFlowStage Stage,
    string AggregateType,
    string AggregateId,
    string? CurrentState,
    string? RequestedAction,
    string CorrelationId,
    string? IdempotencyKey,
    IReadOnlyDictionary<string, string> Metadata);

public sealed class CommerceFlowTracker
{
    private readonly Dictionary<string, string> _metadata =
        new(StringComparer.OrdinalIgnoreCase);

    public CommerceFlowTracker(
        string flowName,
        string aggregateType,
        string aggregateId,
        string requestedAction,
        string? correlationId = null,
        string? idempotencyKey = null)
    {
        FlowName = Require(flowName, nameof(flowName));
        AggregateType = Require(aggregateType, nameof(aggregateType));
        AggregateId = Require(aggregateId, nameof(aggregateId));
        RequestedAction = Require(requestedAction, nameof(requestedAction));
        CorrelationId = string.IsNullOrWhiteSpace(correlationId)
            ? Guid.NewGuid().ToString("N")
            : correlationId.Trim();
        IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey)
            ? null
            : idempotencyKey.Trim();
    }

    public string FlowName { get; }
    public CommerceFlowStage Stage { get; private set; } = CommerceFlowStage.Initialize;
    public string AggregateType { get; }
    public string AggregateId { get; }
    public string? CurrentState { get; private set; }
    public string RequestedAction { get; }
    public string CorrelationId { get; }
    public string? IdempotencyKey { get; }

    public CommerceFlowTracker MoveTo(
        CommerceFlowStage stage,
        string? currentState = null)
    {
        Stage = stage;
        if (!string.IsNullOrWhiteSpace(currentState))
        {
            CurrentState = currentState.Trim();
        }

        return this;
    }

    public CommerceFlowTracker AddMetadata(string key, object? value)
    {
        if (string.IsNullOrWhiteSpace(key) || value is null)
        {
            return this;
        }

        _metadata[key.Trim()] = Convert.ToString(
            value,
            System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        return this;
    }

    public CommerceFlowContext Snapshot() => new(
        FlowName,
        Stage,
        AggregateType,
        AggregateId,
        CurrentState,
        RequestedAction,
        CorrelationId,
        IdempotencyKey,
        new Dictionary<string, string>(_metadata, StringComparer.OrdinalIgnoreCase));

    private static string Require(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }

        return value.Trim();
    }
}
