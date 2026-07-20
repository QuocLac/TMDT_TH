using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace WebApplication2.Models
{
    public class Product : BaseEntity
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(255)]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;

        public int CategoryId { get; set; }

        public Category Category { get; set; } = null!;

        public int? BrandId { get; set; }

        public Brand? Brand { get; set; }

        [Required, MaxLength(255)]
        public string Slug { get; set; } = string.Empty;

        [MaxLength(255)]
        public string? MetaTitle { get; set; }

        [MaxLength(500)]
        public string? MetaDescription { get; set; }

        [MaxLength(255)]
        public string? MetaKeywords { get; set; }

        public ICollection<ProductVariant> Variants { get; set; } = [];

        public ICollection<ProductPromotion> ProductPromotions { get; set; } = [];

        public ICollection<ProductImage> Images { get; set; } = [];

        public ICollection<ProductAttributeValue> AttributeValues { get; set; } = [];

        public ICollection<ProductOptionGroup> OptionGroups { get; set; } = [];

        public ICollection<ProductReview> Reviews { get; set; } = [];
    }
}
