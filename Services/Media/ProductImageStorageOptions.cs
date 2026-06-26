namespace WebApplication2.Services.Media;

public sealed class ProductImageStorageOptions
{
    public const string SectionName = "Media:ProductImages";
    public const long DefaultMaxFileSizeBytes = 5 * 1024 * 1024;

    public string RootDirectory { get; set; } = "wwwroot/uploads/products";

    public string PublicBasePath { get; set; } = "/uploads/products";

    public long MaxFileSizeBytes { get; set; } = DefaultMaxFileSizeBytes;
}
