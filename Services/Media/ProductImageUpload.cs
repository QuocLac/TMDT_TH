namespace WebApplication2.Services.Media;

public sealed record ProductImageUpload(
    Stream Content,
    long Length,
    string FileName,
    string? ContentType);
