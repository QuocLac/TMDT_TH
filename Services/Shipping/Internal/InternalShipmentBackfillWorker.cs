using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Models.Enums;

namespace WebApplication2.Services.Shipping.Internal;

public sealed class InternalShipmentBackfillWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<InternalShipmentBackfillWorker> _logger;

    public InternalShipmentBackfillWorker(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<InternalShipmentBackfillWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider
                .GetRequiredService<ApplicationDbContext>();

            var shipments = await context.Shipments
                .Include(item => item.ReturnRequest)
                .Where(item =>
                    item.Provider != "FastBuy"
                    || item.ExternalOrderCode != null
                    || item.ProviderStatus != null
                    || (item.TrackingCode != null
                        && (!item.TrackingCode.StartsWith("FB")
                            || item.TrackingCode.StartsWith("FB-")))
                    || (item.TrackingCode == null
                        && (item.Status == ShipmentStatus.Created
                            || item.Status == ShipmentStatus.InTransit
                            || item.Status == ShipmentStatus.Delivered
                            || item.Status == ShipmentStatus.DeliveryFailed
                            || item.Status == ShipmentStatus.Returning
                            || item.Status == ShipmentStatus.Returned)))
                .OrderBy(item => item.Id)
                .ToArrayAsync(stoppingToken);

            if (shipments.Length == 0)
            {
                return;
            }

            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

            foreach (var shipment in shipments)
            {
                var wasExternal = !string.Equals(
                    shipment.Provider,
                    "FastBuy",
                    StringComparison.OrdinalIgnoreCase);

                shipment.Provider = "FastBuy";
                shipment.ProviderStatus = null;
                shipment.ExternalOrderCode = null;
                shipment.LastSyncedAt = null;
                shipment.ShipperName = null;
                shipment.ShipperPhone = null;
                shipment.CurrentHub = null;

                if (string.IsNullOrWhiteSpace(shipment.ServiceName)
                    || shipment.ServiceName.Contains(
                        "GHN",
                        StringComparison.OrdinalIgnoreCase))
                {
                    shipment.ServiceName = shipment.Direction
                        == ShipmentDirection.Return
                            ? "Tiếp nhận hàng hoàn"
                            : "Giao hàng nhanh";
                }

                if (NeedsTrackingCode(shipment.Status)
                    && (wasExternal
                        || !InternalTrackingCode.IsValid(shipment.TrackingCode)))
                {
                    shipment.TrackingCode = shipment.Direction
                        == ShipmentDirection.Return
                        && shipment.ReturnRequest is not null
                            ? InternalTrackingCode.ForReturn(
                                shipment.ReturnRequest,
                                nowUtc)
                            : InternalTrackingCode.ForOutbound(
                                shipment,
                                nowUtc);
                }

                shipment.UpdatedAt = nowUtc;
            }

            await context.SaveChangesAsync(stoppingToken);

            _logger.LogInformation(
                "Normalized {ShipmentCount} shipment records for internal fulfillment.",
                shipments.Length);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unable to normalize existing shipment records.");
        }
    }

    private static bool NeedsTrackingCode(ShipmentStatus status) =>
        status is ShipmentStatus.Created
            or ShipmentStatus.InTransit
            or ShipmentStatus.Delivered
            or ShipmentStatus.DeliveryFailed
            or ShipmentStatus.Returning
            or ShipmentStatus.Returned;
}
