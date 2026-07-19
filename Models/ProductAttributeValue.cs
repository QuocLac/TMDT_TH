using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models.Configuration;

namespace WebApplication2.Models;

[EntityTypeConfiguration(typeof(ProductAttributeValueConfiguration))]
[Index(nameof(ProductId), nameof(AttributeDefinitionId), IsUnique = true)]
public sealed class ProductAttributeValue : BaseEntity
{
    public long Id { get; set; }

    public int ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public int AttributeDefinitionId { get; set; }

    public ProductAttributeDefinition AttributeDefinition { get; set; } = null!;

    [MaxLength(2_000)]
    public string? TextValue { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal? NumberValue { get; set; }

    public bool? BooleanValue { get; set; }

    public DateTime? DateValue { get; set; }

    public int? OptionId { get; set; }

    public ProductAttributeOption? Option { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = [];
}
