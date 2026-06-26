using System.Buffers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WebApplication2.Services.Media;

public sealed class LocalProductImageStorage : IProductImageStorage
{
    private const int CopyBufferSize = 81920;

    private static readonly byte[] PngSignature =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A
    ];

    private readonly string _rootDirectory;
    private readonly string _publicBasePath;
    private readonly long _maxFileSizeBytes;
    private readonly ILogger<LocalProductImageStorage> _logger;

    public LocalProductImageStorage(
        IOptions<ProductImageStorageOptions> optionsAccessor,
        IHostEnvironment environment,
        ILogger<LocalProductImageStorage> logger)
    {
        ArgumentNullException.ThrowIfNull(optionsAccessor);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(logger);

        var options = optionsAccessor.Value;
        _maxFileSizeBytes = ValidateMaxFileSize(options.MaxFileSizeBytes);
        _rootDirectory = ResolveRootDirectory(
            environment.ContentRootPath,
            options.RootDirectory);
        _publicBasePath = NormalizePublicBasePath(options.PublicBasePath);
        _logger = logger;
    }

    public async Task<StoredProductImage> SaveAsync(
        ProductImageUpload upload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upload);
        cancellationToken.ThrowIfCancellationRequested();

        ValidateUploadMetadata(upload);
        string? temporaryPath = null;

        try
        {
            var imageType = ResolveImageType(upload.FileName);
            ValidateContentType(upload.ContentType, imageType);

            var header = new byte[imageType.HeaderLength];
            var headerLength = await ReadHeaderAsync(
                upload.Content,
                header,
                cancellationToken);

            if (headerLength != header.Length || !HasExpectedSignature(header, imageType.Kind))
            {
                throw new ProductImageValidationException(
                    ProductImageValidationError.SignatureMismatch,
                    "Nội dung tệp không phải là ảnh JPG, PNG hoặc WEBP hợp lệ.");
            }

            Directory.CreateDirectory(_rootDirectory);

            var storageKey = $"{Guid.NewGuid():N}{imageType.OutputExtension}";
            var finalPath = ResolvePathInsideRoot(storageKey);
            temporaryPath = ResolvePathInsideRoot($".{Guid.NewGuid():N}.uploading");

            var fileOptions = new FileStreamOptions
            {
                Access = FileAccess.Write,
                Mode = FileMode.CreateNew,
                Share = FileShare.None,
                BufferSize = CopyBufferSize,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            };

            long actualLength;
            await using (var destination = new FileStream(temporaryPath, fileOptions))
            {
                await destination.WriteAsync(header, cancellationToken);
                actualLength = await CopyRemainingContentAsync(
                    upload.Content,
                    destination,
                    header.Length,
                    cancellationToken);

                if (actualLength != upload.Length)
                {
                    throw new ProductImageValidationException(
                        ProductImageValidationError.LengthMismatch,
                        "Dữ liệu tệp tải lên không đầy đủ hoặc không hợp lệ.");
                }

                await destination.FlushAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, finalPath);
            temporaryPath = null;

            return new StoredProductImage(
                storageKey,
                $"{_publicBasePath}/{storageKey}",
                imageType.ContentType,
                actualLength);
        }
        catch (ProductImageValidationException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new ProductImageStorageException(exception);
        }
        finally
        {
            if (temporaryPath is not null)
            {
                TryDeleteTemporaryFile(temporaryPath);
            }
        }
    }

    public Task<bool> DeleteAsync(
        string storageReference,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var storageKey = ExtractStorageKey(storageReference);
        var filePath = ResolvePathInsideRoot(storageKey);

        try
        {
            var existed = File.Exists(filePath);
            File.Delete(filePath);
            return Task.FromResult(existed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new ProductImageStorageException(exception);
        }
    }

    private void ValidateUploadMetadata(ProductImageUpload upload)
    {
        if (upload.Content is null || !upload.Content.CanRead)
        {
            throw new ProductImageValidationException(
                ProductImageValidationError.InvalidStream,
                "Không thể đọc tệp ảnh đã chọn.");
        }

        if (upload.Length <= 0)
        {
            throw new ProductImageValidationException(
                ProductImageValidationError.EmptyFile,
                "Vui lòng chọn tệp ảnh có nội dung.");
        }

        if (upload.Length > _maxFileSizeBytes)
        {
            ThrowFileTooLarge();
        }
    }

    private async Task<long> CopyRemainingContentAsync(
        Stream source,
        Stream destination,
        long bytesAlreadyWritten,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        var totalBytes = bytesAlreadyWritten;

        try
        {
            int bytesRead;
            while ((bytesRead = await source.ReadAsync(
                       buffer.AsMemory(0, buffer.Length),
                       cancellationToken)) > 0)
            {
                totalBytes += bytesRead;
                if (totalBytes > _maxFileSizeBytes)
                {
                    ThrowFileTooLarge();
                }

                await destination.WriteAsync(
                    buffer.AsMemory(0, bytesRead),
                    cancellationToken);
            }

            return totalBytes;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void ThrowFileTooLarge()
    {
        var maxMegabytes = Math.Round(
            _maxFileSizeBytes / 1024d / 1024d,
            2,
            MidpointRounding.AwayFromZero);

        throw new ProductImageValidationException(
            ProductImageValidationError.FileTooLarge,
            $"Ảnh vượt quá dung lượng tối đa {maxMegabytes} MB.");
    }

    private static async Task<int> ReadHeaderAsync(
        Stream source,
        byte[] header,
        CancellationToken cancellationToken)
    {
        var totalBytesRead = 0;

        while (totalBytesRead < header.Length)
        {
            var bytesRead = await source.ReadAsync(
                header.AsMemory(totalBytesRead, header.Length - totalBytesRead),
                cancellationToken);

            if (bytesRead == 0)
            {
                break;
            }

            totalBytesRead += bytesRead;
        }

        return totalBytesRead;
    }

    private static ImageType ResolveImageType(string originalFileName)
    {
        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            throw UnsupportedExtension();
        }

        string extension;
        try
        {
            var leafName = Path.GetFileName(originalFileName.Replace('\\', '/'));
            extension = Path.GetExtension(leafName).ToLowerInvariant();
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException)
        {
            throw UnsupportedExtension();
        }

        return extension switch
        {
            ".jpg" or ".jpeg" => new ImageType(
                ImageKind.Jpeg,
                ".jpg",
                "image/jpeg",
                3),
            ".png" => new ImageType(
                ImageKind.Png,
                ".png",
                "image/png",
                PngSignature.Length),
            ".webp" => new ImageType(
                ImageKind.WebP,
                ".webp",
                "image/webp",
                12),
            _ => throw UnsupportedExtension()
        };
    }

    private static void ValidateContentType(
        string? contentType,
        ImageType imageType)
    {
        var normalizedContentType = contentType?
            .Split(';', 2, StringSplitOptions.TrimEntries)[0];

        if (!string.Equals(
                normalizedContentType,
                imageType.ContentType,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ProductImageValidationException(
                ProductImageValidationError.ContentTypeMismatch,
                "Loại nội dung của tệp không khớp với định dạng ảnh.");
        }
    }

    private static bool HasExpectedSignature(
        ReadOnlySpan<byte> header,
        ImageKind imageKind)
    {
        return imageKind switch
        {
            ImageKind.Jpeg => header.Length >= 3 &&
                              header[0] == 0xFF &&
                              header[1] == 0xD8 &&
                              header[2] == 0xFF,
            ImageKind.Png => header.SequenceEqual(PngSignature),
            ImageKind.WebP => header.Length >= 12 &&
                              header[..4].SequenceEqual("RIFF"u8) &&
                              header.Slice(8, 4).SequenceEqual("WEBP"u8),
            _ => false
        };
    }

    private string ExtractStorageKey(string storageReference)
    {
        if (string.IsNullOrWhiteSpace(storageReference) ||
            storageReference.Contains('\\') ||
            storageReference.Contains('?') ||
            storageReference.Contains('#'))
        {
            throw InvalidStorageReference();
        }

        var candidate = storageReference;
        if (candidate.StartsWith('/'))
        {
            var expectedPrefix = $"{_publicBasePath}/";
            if (!candidate.StartsWith(expectedPrefix, StringComparison.Ordinal))
            {
                throw InvalidStorageReference();
            }

            candidate = candidate[expectedPrefix.Length..];
        }

        if (candidate.Contains('/') || !IsGeneratedStorageKey(candidate))
        {
            throw InvalidStorageReference();
        }

        return candidate;
    }

    private static bool IsGeneratedStorageKey(string storageKey)
    {
        var extension = Path.GetExtension(storageKey).ToLowerInvariant();
        if (extension is not (".jpg" or ".png" or ".webp"))
        {
            return false;
        }

        var identifier = Path.GetFileNameWithoutExtension(storageKey);
        return Guid.TryParseExact(identifier, "N", out _);
    }

    private string ResolvePathInsideRoot(string fileName)
    {
        var fullPath = Path.GetFullPath(Path.Combine(_rootDirectory, fileName));
        var relativePath = Path.GetRelativePath(_rootDirectory, fullPath);

        if (Path.IsPathRooted(relativePath) ||
            relativePath.Equals("..", StringComparison.Ordinal) ||
            relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw InvalidStorageReference();
        }

        return fullPath;
    }

    private void TryDeleteTemporaryFile(string temporaryPath)
    {
        try
        {
            File.Delete(temporaryPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.LogWarning(
                exception,
                "Unable to remove temporary product image file {TemporaryFileName}.",
                Path.GetFileName(temporaryPath));
        }
    }

    private static long ValidateMaxFileSize(long maxFileSizeBytes)
    {
        if (maxFileSizeBytes <= 0)
        {
            throw new InvalidOperationException(
                $"{nameof(ProductImageStorageOptions.MaxFileSizeBytes)} must be greater than zero.");
        }

        return maxFileSizeBytes;
    }

    private static string ResolveRootDirectory(
        string contentRootPath,
        string configuredRootDirectory)
    {
        if (string.IsNullOrWhiteSpace(contentRootPath))
        {
            throw new InvalidOperationException("The application content root is not configured.");
        }

        if (string.IsNullOrWhiteSpace(configuredRootDirectory) ||
            Path.IsPathRooted(configuredRootDirectory))
        {
            throw new InvalidOperationException(
                $"{nameof(ProductImageStorageOptions.RootDirectory)} must be a relative directory inside the application content root.");
        }

        var fullContentRoot = Path.GetFullPath(contentRootPath);
        var fullStorageRoot = Path.GetFullPath(
            Path.Combine(fullContentRoot, configuredRootDirectory));
        var relativePath = Path.GetRelativePath(fullContentRoot, fullStorageRoot);

        if (relativePath.Equals(".", StringComparison.Ordinal) ||
            Path.IsPathRooted(relativePath) ||
            relativePath.Equals("..", StringComparison.Ordinal) ||
            relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{nameof(ProductImageStorageOptions.RootDirectory)} must resolve to a child directory of the application content root.");
        }

        return fullStorageRoot;
    }

    private static string NormalizePublicBasePath(string publicBasePath)
    {
        if (string.IsNullOrWhiteSpace(publicBasePath) ||
            !publicBasePath.StartsWith('/') ||
            publicBasePath.Contains('\\') ||
            publicBasePath.Contains('?') ||
            publicBasePath.Contains('#'))
        {
            throw new InvalidOperationException(
                $"{nameof(ProductImageStorageOptions.PublicBasePath)} must be a local absolute URL path.");
        }

        var segments = publicBasePath.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length == 0 ||
            segments.Any(segment => segment is "." or ".."))
        {
            throw new InvalidOperationException(
                $"{nameof(ProductImageStorageOptions.PublicBasePath)} contains an invalid path segment.");
        }

        return $"/{string.Join('/', segments)}";
    }

    private static ProductImageValidationException UnsupportedExtension()
    {
        return new ProductImageValidationException(
            ProductImageValidationError.UnsupportedExtension,
            "Chỉ chấp nhận ảnh JPG, JPEG, PNG hoặc WEBP.");
    }

    private static ProductImageValidationException InvalidStorageReference()
    {
        return new ProductImageValidationException(
            ProductImageValidationError.InvalidStorageReference,
            "Đường dẫn ảnh lưu trữ không hợp lệ.");
    }

    private enum ImageKind
    {
        Jpeg,
        Png,
        WebP
    }

    private sealed record ImageType(
        ImageKind Kind,
        string OutputExtension,
        string ContentType,
        int HeaderLength);
}
