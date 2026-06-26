using WebApplication2.Models.Enums;

namespace WebApplication2.Services.Pricing;

public static class PriceCampaignLifecycle
{
    public static PriceCampaignStatus ResolveConfirmedStatus(
        DateTime startDateUtc,
        DateTime? endDateUtc,
        DateTime nowUtc)
    {
        if (endDateUtc.HasValue && endDateUtc.Value <= nowUtc)
        {
            return PriceCampaignStatus.Completed;
        }

        return startDateUtc > nowUtc
            ? PriceCampaignStatus.Scheduled
            : PriceCampaignStatus.Active;
    }

    public static bool CanAffectPrice(PriceCampaignStatus status)
    {
        return status is PriceCampaignStatus.Confirmed
            or PriceCampaignStatus.Scheduled
            or PriceCampaignStatus.Active;
    }

    public static bool IsTerminal(PriceCampaignStatus status)
    {
        return status is PriceCampaignStatus.Completed
            or PriceCampaignStatus.Cancelled
            or PriceCampaignStatus.Superseded;
    }

    public static bool IsCompatibilityActive(PriceCampaignStatus status)
    {
        return status is PriceCampaignStatus.Confirmed
            or PriceCampaignStatus.Scheduled
            or PriceCampaignStatus.Active;
    }
}
