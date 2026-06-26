using Microsoft.AspNetCore.Http;

namespace WebApplication2.Services.Media;

public static class ProductImageStorageExtensions
{
    public static async Task<StoredProductImage> SaveAsync(
        this IProductImageStorage storage,
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(file);

        await using var content = file.OpenReadStream();
        var upload = new ProductImageUpload(
            content,
            file.Length,
            file.FileName,
            file.ContentType);

        return await storage.SaveAsync(upload, cancellationToken);
    }
}
