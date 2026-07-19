using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models.Configuration;

namespace WebApplication2.Models;

[EntityTypeConfiguration(typeof(CategoryProductAttributeConfiguration))]
[PrimaryKey(nameof(CategoryId), nameof(AttributeDefinitionId))]
public sealed class CategoryProductAttribute
{
    public int CategoryId { get; set; }

    public Category Category { get; set; } = null!;

    public int AttributeDefinitionId { get; set; }

    public ProductAttributeDefinition AttributeDefinition { get; set; } = null!;

    [MaxLength(100)]
    public string? GroupName { get; set; }

    public bool IsRequired { get; set; }

    [Range(0, 9_999)]
    public int DisplayOrder { get; set; }
}
