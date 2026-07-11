using System.ComponentModel.DataAnnotations;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Commerce.Returns;

namespace WebApplication2.ViewModels.Storefront.Returns;

public sealed class ReturnRequestPageViewModel
{
    public ReturnEligibilitySnapshot Eligibility { get; init; } = null!;
    public CreateReturnRequestInput Form { get; init; } = new();
    public string? ErrorMessage { get; init; }
}

public sealed class CreateReturnRequestInput
{
    [Required]
    public Guid OrderPublicToken { get; set; }

    [Required, StringLength(50)]
    public string ReasonCode { get; set; } = "ChangedMind";

    [Required, StringLength(500, MinimumLength = 5)]
    public string ReasonText { get; set; } = string.Empty;

    [StringLength(4000)]
    public string? EvidenceUrls { get; set; }

    [Required, StringLength(128, MinimumLength = 16)]
    public string IdempotencyKey { get; set; } = Guid.NewGuid().ToString("N");

    [MinLength(1)]
    public List<CreateReturnLineInput> Lines { get; set; } = [];
}

public sealed class CreateReturnLineInput
{
    [Range(1, int.MaxValue)]
    public int OrderItemId { get; set; }

    [Range(0, 99)]
    public int Quantity { get; set; }
}

public sealed class ReturnConfirmationViewModel
{
    public string ReturnCode { get; init; } = string.Empty;
    public string OrderCode { get; init; } = string.Empty;
    public Guid OrderPublicToken { get; init; }
    public ReturnRequestStatus Status { get; init; }
    public string ReasonCode { get; init; } = string.Empty;
    public string ReasonText { get; init; } = string.Empty;
    public DateTime RequestedAt { get; init; }
    public DateTime ReturnWindowExpiresAt { get; init; }
    public string? TrackingCode { get; init; }
    public IReadOnlyList<ReturnConfirmationLineViewModel> Items { get; init; } = [];
}

public sealed class ReturnConfirmationLineViewModel
{
    public string ProductName { get; init; } = string.Empty;
    public string Sku { get; init; } = string.Empty;
    public string? VariantDescription { get; init; }
    public int RequestedQuantity { get; init; }
    public int ApprovedQuantity { get; init; }
    public int ReceivedQuantity { get; init; }
    public decimal RefundAmount { get; init; }
}
