using System.ComponentModel.DataAnnotations;

namespace WebApplication2.Models
{
    public class ProductImage : BaseEntity
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string ImageUrl { get; set; }

        public bool IsMain { get; set; }

        // Foreign Key
        public int ProductId { get; set; }
        public Product Product { get; set; }
    }
}