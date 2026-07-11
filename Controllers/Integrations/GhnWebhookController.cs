using Microsoft.AspNetCore.Mvc;
using WebApplication2.Services.Shipping.Ghn;

namespace WebApplication2.Controllers.Integrations;

[ApiController]
[Route("integrations/ghn/webhook")]
[IgnoreAntiforgeryToken]
public sealed class GhnWebhookController : ControllerBase
{
    private const int MaximumPayloadBytes = 256 * 1024;
    private readonly IGhnShippingWebhookProcessor _processor;

    public GhnWebhookController(IGhnShippingWebhookProcessor processor)
    {
        _processor = processor;
    }

    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        if (Request.ContentLength is > MaximumPayloadBytes)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        using var reader = new StreamReader(Request.Body);
        var rawPayload = await reader.ReadToEndAsync(cancellationToken);
        if (rawPayload.Length > MaximumPayloadBytes)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var suppliedSecret =
            Request.Headers["X-FastBuy-GHN-Secret"].FirstOrDefault()
            ?? Request.Query["secret"].FirstOrDefault();

        var result = await _processor.ProcessAsync(
            rawPayload,
            suppliedSecret,
            cancellationToken);

        if (!result.Success)
        {
            return result.Message.Contains("secret", StringComparison.OrdinalIgnoreCase)
                ? Unauthorized(new { success = false, message = result.Message })
                : BadRequest(new { success = false, message = result.Message });
        }

        return Ok(new
        {
            success = true,
            duplicate = result.Duplicate,
            ignored = result.Ignored,
            message = result.Message
        });
    }
}
