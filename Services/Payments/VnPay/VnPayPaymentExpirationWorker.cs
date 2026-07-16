using Microsoft.Extensions.Options;

namespace WebApplication2.Services.Payments.VnPay;

public sealed class VnPayPaymentExpirationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly VnPayOptions _options;
    private readonly ILogger<VnPayPaymentExpirationWorker> _logger;

    public VnPayPaymentExpirationWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<VnPayOptions> options,
        ILogger<VnPayPaymentExpirationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();

                var paymentService =
                    scope.ServiceProvider
                        .GetRequiredService<IVnPayPaymentService>();

                var expiredCount =
                    await paymentService.ExpirePendingPaymentsAsync(
                        stoppingToken);

                if (expiredCount > 0)
                {
                    _logger.LogInformation(
                        "Expired {ExpiredCount} pending VNPay payments.",
                        expiredCount);
                }
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "VNPay payment expiration worker failed.");
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(
                        Math.Clamp(
                            _options.ExpirationPollSeconds,
                            15,
                            300)),
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
