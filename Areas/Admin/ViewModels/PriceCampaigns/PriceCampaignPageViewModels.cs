using Microsoft.AspNetCore.Mvc.Rendering;
using WebApplication2.Models.Enums;

namespace WebApplication2.Areas.Admin.ViewModels.PriceCampaigns;

public sealed class PriceCampaignIndexPageViewModel
{
    public DateTime UtcNow { get; init; }

    public IReadOnlyList<PriceCampaignListItemViewModel> Items { get; init; }
        = [];
}

public sealed class PriceCampaignListItemViewModel
{
    public int Id { get; init; }

    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public DateTime StartDateUtc { get; init; }

    public DateTime? EndDateUtc { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime? ConfirmedAtUtc { get; init; }

    public DateTime? CancelledAtUtc { get; init; }

    public PriceCampaignMode Mode { get; init; }

    public PriceCampaignStatus Status { get; init; }

    public PriceChangeSourceType SourceType { get; init; }

    public PriceConflictPolicy ConflictPolicy { get; init; }

    public int VariantCount { get; init; }

    public int? SupersededByCampaignId { get; init; }

    public string CreatedBy { get; init; } = string.Empty;

    public string RowVersion { get; init; } = string.Empty;
}

public sealed class PriceCampaignEditorPageViewModel
{
    public int CampaignId { get; init; }

    public IReadOnlyList<SelectListItem> CategoryOptions { get; init; }
        = [];

    public IReadOnlyList<SelectListItem> BrandOptions { get; init; }
        = [];
}

public sealed class ApplyPriceCampaignRequest
{
    public int Id { get; set; }

    public string RowVersion { get; set; } = string.Empty;
}
