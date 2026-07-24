using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using WebApplication2.Models;

namespace WebApplication2.Services.Shipping.Internal;

public sealed class InternalShipmentProviderInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        NormalizeShipments(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        NormalizeShipments(eventData.Context);
        return base.SavingChangesAsync(
            eventData,
            result,
            cancellationToken);
    }

    private static void NormalizeShipments(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries<Shipment>()
                     .Where(item => item.State is EntityState.Added or EntityState.Modified))
        {
            var currentProvider = entry.Entity.Provider;
            var originalProvider = entry.State == EntityState.Modified
                ? entry.OriginalValues.GetValue<string>(nameof(Shipment.Provider))
                : null;

            var wasLegacyExternal =
                string.Equals(
                    currentProvider,
                    "GHN",
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    originalProvider,
                    "GHN",
                    StringComparison.OrdinalIgnoreCase);

            var shouldNormalize =
                string.IsNullOrWhiteSpace(currentProvider)
                || wasLegacyExternal
                || string.Equals(
                    currentProvider,
                    "FastBuy",
                    StringComparison.OrdinalIgnoreCase);

            if (!shouldNormalize)
            {
                continue;
            }

            entry.Entity.Provider = "FastBuy";
            entry.Entity.ProviderStatus = null;
            entry.Entity.ExternalOrderCode = null;
            entry.Entity.LastSyncedAt = null;
            entry.Entity.ShipperName = null;
            entry.Entity.ShipperPhone = null;
            entry.Entity.CurrentHub = null;

            if (wasLegacyExternal
                && !InternalTrackingCode.IsValid(entry.Entity.TrackingCode))
            {
                entry.Entity.TrackingCode = null;
            }

            if (string.IsNullOrWhiteSpace(entry.Entity.ServiceName)
                || entry.Entity.ServiceName.Contains(
                    "GHN",
                    StringComparison.OrdinalIgnoreCase))
            {
                entry.Entity.ServiceName = entry.Entity.Direction
                    == WebApplication2.Models.Enums.ShipmentDirection.Return
                        ? "Tiếp nhận hàng hoàn"
                        : "Giao hàng nhanh";
            }
        }
    }

}
