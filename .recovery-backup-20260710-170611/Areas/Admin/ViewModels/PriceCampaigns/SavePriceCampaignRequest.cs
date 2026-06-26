using System.ComponentModel.DataAnnotations;

namespace WebApplication2.Areas.Admin.ViewModels.PriceCampaigns;

public sealed class SavePriceCampaignRequest
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên chiến dịch.")]
    [StringLength(255, ErrorMessage = "Tên chiến dịch không được vượt quá 255 ký tự.")]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime StartDate { get; set; }

    public DateTime EndDate { get; set; }

    [MinLength(1, ErrorMessage = "Vui lòng chọn ít nhất một biến thể.")]
    public List<SavePriceCampaignItemRequest> Items { get; set; } = [];

    // Optional until the current view starts round-tripping this value.
    public string? RowVersion { get; set; }
}

public sealed class SavePriceCampaignItemRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "ID biến thể không hợp lệ.")]
    public int VariantId { get; set; }

    public decimal NewPrice { get; set; }
}
