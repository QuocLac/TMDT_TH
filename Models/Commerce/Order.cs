using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WebApplication2.Models.Enums;

namespace WebApplication2.Models;

public sealed class Order : BaseEntity
{
    public int Id { get; set; }

    [Required, MaxLength(30)]
    public string Code { get; set; } = string.Empty;

    public Guid PublicToken { get; set; } = Guid.NewGuid();

    [Required, MaxLength(64)]
    public string ClientRequestId { get; set; } = string.Empty;

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    [Required, MaxLength(100)]
    public string CustomerName { get; set; } = string.Empty;

    [Required, MaxLength(150), EmailAddress]
    public string CustomerEmail { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string CustomerPhone { get; set; } = string.Empty;

    [Required, MaxLength(255)]
    public string ShippingAddressLine { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string ShippingWard { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string ShippingDistrict { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string ShippingCity { get; set; } = string.Empty;

    public int? ShippingProvinceId { get; set; }
    public int? ShippingDistrictId { get; set; }

    [MaxLength(30)]
    public string? ShippingWardCode { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Subtotal { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal ShippingFee { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal DiscountTotal { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TaxTotal { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal GrandTotal { get; set; }

    [Required, MaxLength(3)]
    public string Currency { get; set; } = "VND";

    public OrderStatus OrderStatus { get; set; } = OrderStatus.PendingPayment;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
    public FulfillmentStatus FulfillmentStatus { get; set; } = FulfillmentStatus.Unfulfilled;

    public DateTime? PlacedAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    [MaxLength(500)]
    public string? CancelReason { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = [];

    public ICollection<OrderItem> Items { get; set; } = [];
    public ICollection<PaymentTransaction> PaymentTransactions { get; set; } = [];
    public ICollection<Shipment> Shipments { get; set; } = [];
    public ICollection<StockReservation> StockReservations { get; set; } = [];
    public ICollection<OrderStatusHistory> StatusHistory { get; set; } = [];
    public ICollection<OrderCancellationRequest> CancellationRequests { get; set; } = [];
}
