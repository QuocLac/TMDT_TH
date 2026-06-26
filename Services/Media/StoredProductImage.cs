namespace WebApplication2.Services.Media;

public sealed record StoredProductImage(
    string StorageKey,
    string PublicPath,
    string ContentType,
    long Length);
