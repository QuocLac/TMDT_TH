using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WebApplication2.Models.Enums;

namespace WebApplication2.Models
{
    public class Promotion : BaseEntity
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(255)]
        public string Name { get; set; }

        [MaxLength(50)]
        public string CouponCode { get; set; }

        public PromotionType Type { get; set; }

        public CustomerTier? TargetTier { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal DiscountValue { get; set; }

        public DiscountUnit Unit { get; set; }

        public int? UsageLimit { get; set; }
        public int UsedCount { get; set; } = 0;

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public bool IsActive { get; set; } = true;

        public ICollection<ProductPromotion> ProductPromotions { get; set; }
        public ICollection<PromotionCustomer> PromotionCustomers { get; set; }
    }
}