using System.ComponentModel.DataAnnotations;

namespace WebApplication2.Models
{
    public class Account : BaseEntity
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(100)]
        public string Username { get; set; }

        [Required]
        public string PasswordHash { get; set; }

        [Required, EmailAddress, MaxLength(150)]
        public string Email { get; set; }

        [Required, MaxLength(500)]
        public string Avt { get; set; } = "/images/default-avatar.png";

        public bool IsActive { get; set; } = true;

        // Navigation property (Quan hệ 1-1 với Customer)
        public Customer Customer { get; set; }
    }
}