using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models.Configuration;

namespace WebApplication2.Models;

[EntityTypeConfiguration(typeof(ProductOptionValueConfiguration))]
public sealed class ProductOptionValue : BaseEntity
{
    public int Id { get; set; }

    public int OptionGroupId { get; set; }

    public ProductOptionGroup OptionGroup { get; set; } = null!;

    [Required, MaxLength(80)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(120)]
    public string Label { get; set; } = string.Empty;

    [Range(0, 9_999)]
    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<ProductVariantOptionSelection> VariantSelections { get; set; } = [];
}
