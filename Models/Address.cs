using System.ComponentModel.DataAnnotations;

namespace WebApplication2.Models
{
    public class Address : BaseEntity
    {
        [Key]
        public int Id { get; set; }

        public int CustomerId { get; set; }
        public Customer Customer { get; set; }

        [Required, MaxLength(255)]
        public string Street { get; set; }

        [Required, MaxLength(100)]
        public string Ward { get; set; }

        [Required, MaxLength(100)]
        public string District { get; set; }

        [Required, MaxLength(100)]
        public string City { get; set; }

        public bool IsDefault { get; set; }
    }
}