namespace WebApplication2.Services.Media;

public enum ProductImageValidationError
{
    EmptyFile,
    FileTooLarge,
    UnsupportedExtension,
    ContentTypeMismatch,
    SignatureMismatch,
    LengthMismatch,
    InvalidStream,
    InvalidStorageReference
}

public sealed class ProductImageValidationException : Exception
{
    public ProductImageValidationException(
        ProductImageValidationError error,
        string userMessage)
        : base(userMessage)
    {
        Error = error;
    }

    public ProductImageValidationError Error { get; }

    public string UserMessage => Message;
}

public sealed class ProductImageStorageException : Exception
{
    public const string SafeMessage = "Không thể xử lý ảnh lúc này. Vui lòng thử lại.";

    public ProductImageStorageException(Exception innerException)
        : base(SafeMessage, innerException)
    {
    }

    public string UserMessage => Message;
}
