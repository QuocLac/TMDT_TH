using System.ComponentModel.DataAnnotations;

namespace WebApplication2.Areas.Admin.ViewModels.Orders;

public sealed class CreateOrderCancellationRequestModel
{
    [Required]
    public string OrderRowVersion { get; set; } = string.Empty;

    [Required, StringLength(50, MinimumLength = 2)]
    public string ReasonCode { get; set; } = string.Empty;

    [Required, StringLength(500, MinimumLength = 3)]
    public string ReasonText { get; set; } = string.Empty;

    [Required, StringLength(128, MinimumLength = 8)]
    public string IdempotencyKey { get; set; } = string.Empty;

    [Required, MinLength(1)]
    public List<OrderCancellationLineRequestModel> Lines { get; set; } = [];
}

public sealed class OrderCancellationLineRequestModel
{
    [Range(1, int.MaxValue)]
    public int OrderItemId { get; set; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; set; }
}

public sealed class ReviewOrderCancellationRequestModel
{
    [Required]
    public string OrderRowVersion { get; set; } = string.Empty;

    [Required]
    public string CancellationRowVersion { get; set; } = string.Empty;

    public bool Approve { get; set; }

    [StringLength(500)]
    public string? ReviewNote { get; set; }
}
