namespace WebApplication2.Services.Media;

public interface IProductImageStorage
{
    Task<StoredProductImage> SaveAsync(
        ProductImageUpload upload,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an image by either the storage key or the public path returned by SaveAsync.
    /// Returns false when the image no longer exists.
    /// </summary>
    Task<bool> DeleteAsync(
        string storageReference,
        CancellationToken cancellationToken = default);
}
