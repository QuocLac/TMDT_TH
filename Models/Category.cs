using System.ComponentModel.DataAnnotations;

namespace WebApplication2.Models;

public sealed class Category : BaseEntity
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(255)]
    public string Name { get; set; } = string.Empty;

    public int? ParentId { get; set; }
    public Category? Parent { get; set; }
    public ICollection<Category> Children { get; set; } = [];

    [Required, MaxLength(255)]
    public string Slug { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string IconKey { get; set; } = "folder";

    [Range(0, 9_999)]
    public int DisplayOrder { get; set; }

    public bool IsVisible { get; set; } = true;

    [MaxLength(255)]
    public string MetaTitle { get; set; } = string.Empty;

    [MaxLength(500)]
    public string MetaDescription { get; set; } = string.Empty;

    public ICollection<Product> Products { get; set; } = [];
}
