namespace WebApplication2.Services.Payments.Simulation;

public sealed record DevelopmentSimulationResult(
    bool Success,
    bool AlreadyProcessed,
    string Code,
    string Message)
{
    public static DevelopmentSimulationResult Failure(
        string code,
        string message) =>
        new(false, false, code, message);

    public static DevelopmentSimulationResult Completed(
        string code,
        string message,
        bool alreadyProcessed = false) =>
        new(true, alreadyProcessed, code, message);
}

public interface IDevelopmentPaymentSimulator
{
    bool IsEnabled { get; }

    Task<DevelopmentSimulationResult> ConfirmVnPayPaymentAsync(
        int orderId,
        string actor,
        CancellationToken cancellationToken);

    Task<DevelopmentSimulationResult> ConfirmVnPayRefundAsync(
        long returnRequestId,
        string actor,
        CancellationToken cancellationToken);

    Task<DevelopmentSimulationResult> CloseRefundAsync(
        long returnRequestId,
        string actor,
        CancellationToken cancellationToken);
}
