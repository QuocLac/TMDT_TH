namespace WebApplication2.Models.Enums;

public enum PriceCampaignMode
{
    FixedWindow = 1,
    OpenEnded = 2
}

public enum PriceCampaignStatus
{
    Draft = 1,
    Confirmed = 2,
    Scheduled = 3,
    Active = 4,
    Completed = 5,
    Cancelled = 6,
    Superseded = 7
}

public enum PriceChangeSourceType
{
    Manual = 1,
    Market = 2,
    Promotion = 3,
    Recovery = 4,
    Legacy = 5,
    System = 6
}

public enum PriceConflictPolicy
{
    Reject = 1,
    ReplaceFromStart = 2,
    SupersedeNow = 3
}

public enum PriceAdjustmentType
{
    FixedPrice = 1,
    PercentOff = 2,
    AmountOff = 3
}

public enum EffectivePriceSourceType
{
    ListPrice = 1,
    Campaign = 2
}

public enum PriceHistoryKind
{
    ListPrice = 1,
    EffectivePrice = 2
}

public enum PriceHistoryEventType
{
    Applied = 1,
    Restored = 2,
    Replaced = 3,
    Cancelled = 4,
    ListPriceChanged = 5,
    EffectivePriceChanged = 6,
    Legacy = 7
}
