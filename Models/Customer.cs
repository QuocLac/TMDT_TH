using System.ComponentModel.DataAnnotations;
using WebApplication2.Models.Enums;

namespace WebApplication2.Models;

public class Customer : BaseEntity
{
    [Key]
    public int Id { get; set; }

    public int AccountId { get; set; }

    public Account Account { get; set; } = null!;

    [Required, MaxLength(100)]
    public string FullName { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string PhoneNumber { get; set; } = string.Empty;

    public CustomerTier Tier { get; set; } = CustomerTier.Standard;

    public ICollection<Address> Addresses { get; set; } = [];

    public ICollection<PromotionCustomer> PromotionCustomers { get; set; } = [];

    public ICollection<ProductReview> Reviews { get; set; } = [];
}
