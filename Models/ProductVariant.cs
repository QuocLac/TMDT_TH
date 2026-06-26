using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WebApplication2.Models
{
    public class ProductVariant : BaseEntity
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(100)]
        public string SKU { get; set; } // Mã lưu kho duy nhất cho từng biến thể

        [MaxLength(50)]
        public string Color { get; set; }

        [MaxLength(50)]
        public string Size { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal Price { get; set; } // Giá bán gốc của biến thể này

        public int StockQuantity { get; set; }

        [MaxLength(500)]
        public string ImageUrl { get; set; }

        // Foreign Key
        public int ProductId { get; set; }
        public Product Product { get; set; }

        public ICollection<PriceHistory> PriceHistories { get; set; }
    }
}