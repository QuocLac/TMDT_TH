using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using WebApplication2.Areas.Admin.ViewModels.Validation;
using WebApplication2.Models.Enums;

namespace WebApplication2.Areas.Admin.ViewModels.PriceCampaigns;

public sealed class SavePriceCampaignRequest
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên kế hoạch giá.")]
    [StringLength(
        255,
        ErrorMessage = "Tên kế hoạch không được vượt quá 255 ký tự.")]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PriceCampaignMode Mode { get; set; }
        = PriceCampaignMode.FixedWindow;

    public DateTime StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập lý do thay đổi giá.")]
    [StringLength(
        500,
        ErrorMessage = "Lý do không được vượt quá 500 ký tự.")]
    public string Reason { get; set; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PriceChangeSourceType SourceType { get; set; }
        = PriceChangeSourceType.Manual;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PriceConflictPolicy ConflictPolicy { get; set; }
        = PriceConflictPolicy.Reject;

    [StringLength(64)]
    public string? ClientRequestId { get; set; }

    [MinLength(1, ErrorMessage = "Vui lòng chọn ít nhất một biến thể.")]
    public List<SavePriceCampaignItemRequest> Items { get; set; } = [];

    [StringLength(200)]
    public string? RowVersion { get; set; }
}

public sealed class SavePriceCampaignItemRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "ID biến thể không hợp lệ.")]
    public int VariantId { get; set; }

    // Compatibility value for the old endpoint. New workflow recalculates this on server.
    [MoneyRange(ErrorMessage = "Giá mới phải lớn hơn 0.")]
    public decimal NewPrice { get; set; } = 1m;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PriceAdjustmentType AdjustmentType { get; set; }
        = PriceAdjustmentType.FixedPrice;

    public decimal AdjustmentValue { get; set; }

    [StringLength(200)]
    public string? VariantRowVersion { get; set; }
}

public sealed class PricePlanPreviewRequest
{
    public int CampaignId { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PriceCampaignMode Mode { get; set; }
        = PriceCampaignMode.FixedWindow;

    public DateTime StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PriceConflictPolicy ConflictPolicy { get; set; }
        = PriceConflictPolicy.Reject;

    [MinLength(1, ErrorMessage = "Vui lòng chọn ít nhất một biến thể.")]
    public List<PricePlanPreviewItemRequest> Items { get; set; } = [];
}

public sealed class PricePlanPreviewItemRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "ID biến thể không hợp lệ.")]
    public int VariantId { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PriceAdjustmentType AdjustmentType { get; set; }
        = PriceAdjustmentType.FixedPrice;

    public decimal AdjustmentValue { get; set; }

    [StringLength(200)]
    public string? VariantRowVersion { get; set; }
}

public sealed class ConfirmPriceCampaignDraftRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "ID kế hoạch không hợp lệ.")]
    public int Id { get; set; }

    [Required(ErrorMessage = "Thiếu phiên bản bản nháp.")]
    [StringLength(200)]
    public string RowVersion { get; set; } = string.Empty;
}
