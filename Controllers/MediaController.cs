using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebApplication2.Services.Media;

namespace WebApplication2.Controllers;

[Authorize]
[Route("media")]
public sealed class MediaController : Controller
{
    private const int MaximumFilesPerRequest = 9;

    private readonly IProductImageStorage _imageStorage;
    private readonly ILogger<MediaController> _logger;

    public MediaController(
        IProductImageStorage imageStorage,
        ILogger<MediaController> logger)
    {
        _imageStorage = imageStorage;
        _logger = logger;
    }

    [HttpPost("images")]
    [RequestFormLimits(MultipartBodyLengthLimit = 52_428_800)]
    public async Task<IActionResult> UploadImages(
        IReadOnlyList<IFormFile> files,
        CancellationToken cancellationToken)
    {
        var selectedFiles = files?
            .Where(file => file is { Length: > 0 })
            .Take(MaximumFilesPerRequest + 1)
            .ToArray()
            ?? [];

        if (selectedFiles.Length == 0)
        {
            return BadRequest(new
            {
                success = false,
                message = "Vui lòng chọn ít nhất một ảnh."
            });
        }

        if (selectedFiles.Length > MaximumFilesPerRequest)
        {
            return BadRequest(new
            {
                success = false,
                message = $"Mỗi lần được chọn tối đa {MaximumFilesPerRequest} ảnh."
            });
        }

        var storedReferences = new List<string>();
        try
        {
            foreach (var file in selectedFiles)
            {
                await using var stream = file.OpenReadStream();
                var stored = await _imageStorage.SaveAsync(
                    new ProductImageUpload(
                        stream,
                        file.Length,
                        file.FileName,
                        file.ContentType),
                    cancellationToken);

                storedReferences.Add(BuildAbsoluteUrl(stored.PublicPath));
            }

            return Json(new
            {
                success = true,
                items = storedReferences.Select(url => new { url })
            });
        }
        catch (ProductImageValidationException exception)
        {
            await DeleteStoredFilesQuietlyAsync(storedReferences);
            return BadRequest(new
            {
                success = false,
                message = exception.UserMessage
            });
        }
        catch (ProductImageStorageException exception)
        {
            await DeleteStoredFilesQuietlyAsync(storedReferences);
            _logger.LogError(exception, "Unable to store customer media images.");
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new
                {
                    success = false,
                    message = "Không thể lưu ảnh lúc này. Vui lòng thử lại."
                });
        }
    }

    private string BuildAbsoluteUrl(string publicPath)
    {
        var scheme = Request.IsHttps ? Request.Scheme : Uri.UriSchemeHttps;
        return $"{scheme}://{Request.Host}{Request.PathBase}{publicPath}";
    }

    private async Task DeleteStoredFilesQuietlyAsync(
        IEnumerable<string> absoluteUrls)
    {
        foreach (var value in absoluteUrls)
        {
            try
            {
                if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
                {
                    await _imageStorage.DeleteAsync(
                        uri.AbsolutePath,
                        CancellationToken.None);
                }
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Unable to roll back uploaded customer media image.");
            }
        }
    }
}
