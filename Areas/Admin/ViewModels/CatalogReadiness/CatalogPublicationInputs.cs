using System.ComponentModel.DataAnnotations;

namespace WebApplication2.Areas.Admin.ViewModels.CatalogReadiness;

public class CatalogPublicationReturnInput
{
    public string? Query { get; set; }

    public string? Status { get; set; }

    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;
}

public sealed class CatalogPublicationBulkInput
    : CatalogPublicationReturnInput
{
    public List<int> ProductIds { get; set; } = [];

    [Required]
    public string Operation { get; set; } = string.Empty;
}
