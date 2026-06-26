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

        // Đổi string thành string?
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;

        public int CategoryId { get; set; }
        public Category Category { get; set; }

        [Required, MaxLength(255)]
        public string Slug { get; set; }

        // Đổi các trường SEO thành string?
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