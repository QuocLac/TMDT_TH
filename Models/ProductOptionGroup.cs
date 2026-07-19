using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models.Configuration;

namespace WebApplication2.Models;

[EntityTypeConfiguration(typeof(ProductOptionGroupConfiguration))]
public sealed class ProductOptionGroup : BaseEntity
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public Product Product { get; set; } = null!;

    [Required, MaxLength(80)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [Range(0, 9_999)]
    public int DisplayOrder { get; set; }

    public bool IsRequired { get; set; } = true;

    public bool IsActive { get; set; } = true;

    public ICollection<ProductOptionValue> Values { get; set; } = [];

    public ICollection<ProductVariantOptionSelection> VariantSelections { get; set; } = [];
}
