using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models.Configuration;

namespace WebApplication2.Models;

[EntityTypeConfiguration(typeof(ProductAttributeOptionConfiguration))]
[Index(nameof(AttributeDefinitionId), nameof(Value), IsUnique = true)]
public sealed class ProductAttributeOption : BaseEntity
{
    public int Id { get; set; }

    public int AttributeDefinitionId { get; set; }

    public ProductAttributeDefinition AttributeDefinition { get; set; } = null!;

    [Required, MaxLength(80)]
    public string Value { get; set; } = string.Empty;

    [Required, MaxLength(150)]
    public string Label { get; set; } = string.Empty;

    [Range(0, 9_999)]
    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<ProductAttributeValue> ProductValues { get; set; } = [];
}
