using Microsoft.AspNetCore.Mvc;
using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Commerce.Flows;
using WebApplication2.Services.Shipping;

namespace WebApplication2.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/Shipping/{id:long}/simulate")]
public sealed class DevelopmentShippingController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IWebHostEnvironment _environment;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DevelopmentShippingController> _logger;

    public DevelopmentShippingController(
        ApplicationDbContext context,
        IWebHostEnvironment environment,
        TimeProvider timeProvider,
        ILogger<DevelopmentShippingController> logger)
    {
        _context = context;
        _environment = environment;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    [HttpPost("")]
    public async Task<IActionResult> Simulate(
        long id,
        [FromBody] DevelopmentShippingSimulationRequest request,
        CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        if (!ModelState.IsValid
            || string.IsNullOrWhiteSpace(request.TargetStatus)
            || !Enum.TryParse<ShipmentStatus>(
                request.TargetStatus,
                ignoreCase: true,
                out var targetStatus))
        {
            return BadRequest(new
            {
                success = false,
                errorCode = "DEV_SHIPPING_INPUT_INVALID",
                message = "Trạng thái giao vận mô phỏng không hợp lệ."
            });
        }

        try
        {
            var result =
                await DevelopmentShippingSimulationService.SimulateAsync(
                    _context,
                    id,
                    targetStatus,
                    ResolveActor(),
                    _environment.IsDevelopment(),
                    _timeProvider,
                    cancellationToken);

            return result.Success
                ? Ok(new
                {
                    success = true,
                    data = new
                    {
                        status = result.Status?.ToString(),
                        result.AlreadyProcessed
                    },
                    message = result.Message
                })
                : UnprocessableEntity(new
                {
                    success = false,
                    errorCode = result.Code,
                    message = result.Message
                });
        }
        catch (CommerceFlowException exception)
        {
            _logger.LogWarning(
                exception,
                "Development shipping simulation failed. "
                + "ErrorCode={ErrorCode}; Stage={Stage}; "
                + "ShipmentId={ShipmentId}; CorrelationId={CorrelationId}",
                exception.ErrorCode,
                exception.FlowContext.Stage,
                id,
                exception.FlowContext.CorrelationId);

            return UnprocessableEntity(new
            {
                success = false,
                errorCode = exception.ErrorCode,
                flowName = exception.FlowContext.FlowName,
                stage = exception.FlowContext.Stage.ToString(),
                correlationId = exception.FlowContext.CorrelationId,
                message = exception.Message
            });
        }
    }

    private string ResolveActor()
    {
        var name = User.Identity?.Name;
        return string.IsNullOrWhiteSpace(name)
            ? "Development Admin"
            : name;
    }
}

public sealed class DevelopmentShippingSimulationRequest
{
    public string TargetStatus { get; set; } = string.Empty;
}
