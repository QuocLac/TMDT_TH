using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models.Enums;
using WebApplication2.Models.Configuration;

namespace WebApplication2.Models;

[EntityTypeConfiguration(typeof(ProductAttributeDefinitionConfiguration))]
[Index(nameof(Code), IsUnique = true)]
public sealed class ProductAttributeDefinition : BaseEntity
{
    public int Id { get; set; }

    [Required, MaxLength(80)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? HelpText { get; set; }

    public ProductAttributeDataType DataType { get; set; }

    [MaxLength(30)]
    public string? Unit { get; set; }

    public bool IsFilterable { get; set; }

    public bool IsComparable { get; set; }

    public bool IsCustomerVisible { get; set; } = true;

    public bool IsActive { get; set; } = true;

    public ICollection<ProductAttributeOption> Options { get; set; } = [];

    public ICollection<CategoryProductAttribute> CategoryAssignments { get; set; } = [];

    public ICollection<ProductAttributeValue> ProductValues { get; set; } = [];
}
