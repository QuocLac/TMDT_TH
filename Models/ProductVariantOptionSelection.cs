using Microsoft.EntityFrameworkCore;
using WebApplication2.Models.Configuration;

namespace WebApplication2.Models;

[EntityTypeConfiguration(typeof(ProductVariantOptionSelectionConfiguration))]
public sealed class ProductVariantOptionSelection
{
    public int VariantId { get; set; }

    public ProductVariant Variant { get; set; } = null!;

    public int OptionGroupId { get; set; }

    public ProductOptionGroup OptionGroup { get; set; } = null!;

    public int OptionValueId { get; set; }

    public ProductOptionValue OptionValue { get; set; } = null!;
}
