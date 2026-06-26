using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Net;
using WebApplication2.Models.Enums;

namespace WebApplication2.Models
{
    public class Customer : BaseEntity
    {
        [Key]
        public int Id { get; set; }

        // Khóa ngoại liên kết 1-1 với Account
        public int AccountId { get; set; }
        public Account Account { get; set; }

        [Required, MaxLength(100)]
        public string FullName { get; set; }

        [MaxLength(20)]
        public string PhoneNumber { get; set; }

        public CustomerTier Tier { get; set; } = CustomerTier.Standard;

        // Navigation properties
        public ICollection<Address> Addresses { get; set; }
        public ICollection<PromotionCustomer> PromotionCustomers { get; set; }
    }
}