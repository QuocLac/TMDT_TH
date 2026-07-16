namespace WebApplication2.Services.Cart;

public sealed partial class SessionCartService
{
    private const string CheckoutRequestSessionKey =
        "FastBuy.Checkout.Request.v1";

    public long GetCartVersion()
    {
        return ReadCartState().Version;
    }

    public string GetOrCreateCheckoutClientRequestId(long cartVersion)
    {
        if (cartVersion < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cartVersion));
        }

        var stored = Session.GetString(CheckoutRequestSessionKey);

        if (!string.IsNullOrWhiteSpace(stored))
        {
            var separatorIndex = stored.IndexOf(':');

            if (separatorIndex > 0
                && long.TryParse(
                    stored[..separatorIndex],
                    out var storedVersion)
                && storedVersion == cartVersion)
            {
                var existingId = stored[(separatorIndex + 1)..];

                if (Guid.TryParseExact(existingId, "N", out _))
                {
                    return existingId;
                }
            }
        }

        var clientRequestId = Guid.NewGuid().ToString("N");

        Session.SetString(
            CheckoutRequestSessionKey,
            $"{cartVersion}:{clientRequestId}");

        return clientRequestId;
    }

    public void CompleteCheckout(
        IReadOnlyCollection<int> purchasedVariantIds,
        string clientRequestId)
    {
        ArgumentNullException.ThrowIfNull(purchasedVariantIds);

        if (!Guid.TryParseExact(clientRequestId, "N", out _))
        {
            throw new ArgumentException(
                "ClientRequestId của checkout không hợp lệ.",
                nameof(clientRequestId));
        }

        var ids = purchasedVariantIds
            .Where(id => id > 0)
            .Distinct()
            .ToHashSet();

        if (ids.Count == 0)
        {
            ResetCheckoutClientRequestId(clientRequestId);
            return;
        }

        var state = ReadCartState();
        var removedCount = state.Lines.RemoveAll(
            line => ids.Contains(line.VariantId));

        if (removedCount > 0)
        {
            state.Version++;
            SaveCartState(state);
        }

        ResetCheckoutClientRequestId(clientRequestId);
    }

    public void ResetCheckoutClientRequestId(
        string? clientRequestId = null)
    {
        var stored = Session.GetString(CheckoutRequestSessionKey);

        if (string.IsNullOrWhiteSpace(stored))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(clientRequestId))
        {
            Session.Remove(CheckoutRequestSessionKey);
            return;
        }

        var separatorIndex = stored.IndexOf(':');
        var storedRequestId = separatorIndex >= 0
            ? stored[(separatorIndex + 1)..]
            : stored;

        if (string.Equals(
                storedRequestId,
                clientRequestId.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            Session.Remove(CheckoutRequestSessionKey);
        }
    }
}
