using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace WebApplication2.Models
{
    public class Product : BaseEntity
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(255)]
        public string Name { get; set; }

        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;

        // Foreign Keys
        public int CategoryId { get; set; }
        public Category Category { get; set; }

        public int? BrandId { get; set; }
        public Brand? Brand { get; set; }

        // SEO Properties
        [Required, MaxLength(255)]
        public string Slug { get; set; }

        [MaxLength(255)]
        public string? MetaTitle { get; set; }
        [MaxLength(500)]
        public string? MetaDescription { get; set; }
        [MaxLength(255)]
        public string? MetaKeywords { get; set; }

        // Navigation
        public ICollection<ProductVariant> Variants { get; set; }
        public ICollection<ProductPromotion> ProductPromotions { get; set; }
        public ICollection<ProductImage> Images { get; set; }
    }
}