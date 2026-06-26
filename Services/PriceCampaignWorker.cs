using System.Data;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Pricing;

namespace WebApplication2.Services;

public sealed class PriceCampaignWorker : BackgroundService
{
    private static readonly TimeSpan ProcessingInterval = TimeSpan.FromMinutes(1);

    private readonly ILogger<PriceCampaignWorker> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeProvider _timeProvider;

    public PriceCampaignWorker(
        IServiceProvider serviceProvider,
        ILogger<PriceCampaignWorker> logger,
        TimeProvider timeProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Price transition worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessCampaignsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (DbUpdateConcurrencyException exception)
            {
                _logger.LogWarning(
                    exception,
                    "Pricing lifecycle encountered a concurrent update and will retry.");
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Pricing lifecycle processing failed.");
            }

            try
            {
                await Task.Delay(
                    ProcessingInterval,
                    _timeProvider,
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ProcessCampaignsAsync(
        CancellationToken cancellationToken)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var context = scope.ServiceProvider
            .GetRequiredService<ApplicationDbContext>();
        var effectivePriceService = scope.ServiceProvider
            .GetRequiredService<IEffectivePriceService>();

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var correlationId = $"price-worker-{nowUtc:yyyyMMddHHmmss}";

        await using var transaction =
            await context.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);

        var lifecycleCampaigns = await context.PriceCampaigns
            .Where(campaign =>
                campaign.Status == PriceCampaignStatus.Confirmed
                || campaign.Status == PriceCampaignStatus.Scheduled
                || campaign.Status == PriceCampaignStatus.Active)
            .Where(campaign =>
                campaign.StartDate <= nowUtc
                || (campaign.EndDate.HasValue
                    && campaign.EndDate.Value <= nowUtc))
            .ToListAsync(cancellationToken);

        var transitionedCount = 0;

        foreach (var campaign in lifecycleCampaigns)
        {
            var nextStatus =
                PriceCampaignLifecycle.ResolveConfirmedStatus(
                    campaign.StartDate,
                    campaign.EndDate,
                    nowUtc);
            var nextIsActive =
                PriceCampaignLifecycle.IsCompatibilityActive(nextStatus);

            if (campaign.Status == nextStatus
                && campaign.IsActive == nextIsActive)
            {
                continue;
            }

            campaign.Status = nextStatus;
            campaign.IsActive = nextIsActive;
            campaign.UpdatedAt = nowUtc;
            transitionedCount++;
        }

        if (transitionedCount > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        var recalculation =
            await effectivePriceService.RecalculateAffectedVariantsAsync(
                "Hệ thống giá",
                "Đồng bộ trạng thái và giá hiệu lực theo thời gian",
                correlationId,
                cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        if (transitionedCount > 0
            || recalculation.ChangedCount > 0)
        {
            _logger.LogInformation(
                "Pricing cycle {CorrelationId}: {TransitionedCampaignCount} campaign transitions, {ChangedVariantCount} variant projections.",
                correlationId,
                transitionedCount,
                recalculation.ChangedCount);
        }
    }
}
