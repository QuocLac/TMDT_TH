using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WebApplication2.Areas.Admin.ViewModels.Shipping;
using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Commerce.Flows;
using WebApplication2.Services.Commerce.Returns;
using WebApplication2.Services.Shipping;
using WebApplication2.Services.Shipping.Ghn;

namespace WebApplication2.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/Shipping")]
public sealed class ShippingController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IShippingExecutionService _shipping;
    private readonly GhnShippingOptions _options;
    private readonly ILogger<ShippingController> _logger;

    public ShippingController(
        ApplicationDbContext context,
        IShippingExecutionService shipping,
        IOptions<GhnShippingOptions> options,
        ILogger<ShippingController> logger)
    {
        _context = context;
        _shipping = shipping;
        _options = options.Value;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? search,
        ShipmentStatus? status,
        CancellationToken cancellationToken)
    {
        var query = _context.Shipments
            .AsNoTracking()
            .Include(item => item.Order)
            .Where(item => item.Direction == ShipmentDirection.Outbound)
            .AsQueryable();

        var normalizedSearch = search?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            query = query.Where(item =>
                item.Order.Code.Contains(normalizedSearch)
                || item.Order.CustomerName.Contains(normalizedSearch)
                || (item.TrackingCode != null && item.TrackingCode.Contains(normalizedSearch)));
        }

        if (status.HasValue)
        {
            query = query.Where(item => item.Status == status.Value);
        }

        var items = await query
            .OrderByDescending(item => item.UpdatedAt ?? item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Take(200)
            .Select(item => new ShippingAdminListItemViewModel
            {
                Id = item.Id,
                OrderId = item.OrderId,
                OrderCode = item.Order.Code,
                CustomerName = item.Order.CustomerName,
                Direction = item.Direction,
                Status = item.Status,
                ProviderStatus = item.ProviderStatus,
                TrackingCode = item.TrackingCode,
                Fee = item.Fee,
                CodAmount = item.CodAmount,
                CreatedAt = item.CreatedAt,
                UpdatedAt = item.UpdatedAt
            })
            .ToArrayAsync(cancellationToken);

        return View(new ShippingAdminIndexViewModel
        {
            Items = items,
            Search = normalizedSearch,
            Status = status
        });
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Details(long id, CancellationToken cancellationToken)
    {
        var shipment = await _context.Shipments
            .AsNoTracking()
            .Include(item => item.Order)
            .SingleOrDefaultAsync(
                item => item.Id == id
                    && item.Direction == ShipmentDirection.Outbound,
                cancellationToken);

        if (shipment is null)
        {
            return NotFound();
        }

        var aggregateId = id.ToString();
        var outbox = await _context.IntegrationOutboxMessages
            .AsNoTracking()
            .Where(item => item.Provider == "GHN"
                && item.AggregateType == "Shipment"
                && item.AggregateId == aggregateId)
            .OrderByDescending(item => item.CreatedAt)
            .Take(30)
            .Select(item => new ShippingOutboxItemViewModel
            {
                Id = item.Id,
                Type = item.MessageType,
                Status = item.Status,
                AttemptCount = item.AttemptCount,
                NextAttemptAt = item.NextAttemptAt,
                LastError = item.LastError,
                CreatedAt = item.CreatedAt
            })
            .ToArrayAsync(cancellationToken);

        var inbox = string.IsNullOrWhiteSpace(shipment.ExternalOrderCode)
            ? Array.Empty<ShippingInboxItemViewModel>()
            : await _context.IntegrationInboxEvents
                .AsNoTracking()
                .Where(item => item.Provider == "GHN"
                    && (item.CorrelationId == shipment.ExternalOrderCode
                        || (item.ExternalEventId != null
                            && item.ExternalEventId.Contains(shipment.ExternalOrderCode))))
                .OrderByDescending(item => item.ReceivedAt)
                .Take(30)
                .Select(item => new ShippingInboxItemViewModel
                {
                    Id = item.Id,
                    Type = item.EventType,
                    Status = item.Status,
                    ExternalEventId = item.ExternalEventId,
                    ProviderOccurredAt = item.ProviderOccurredAt,
                    LastError = item.LastError,
                    ReceivedAt = item.ReceivedAt
                })
                .ToArrayAsync(cancellationToken);

        var hasPendingOrderCancellation = await _context.Set<OrderCancellationRequest>()
            .AsNoTracking()
            .AnyAsync(
                item => item.OrderId == shipment.OrderId
                    && item.Status == OrderCancellationStatus.Pending,
                cancellationToken);

        var model = new ShippingAdminDetailsViewModel
        {
            Id = shipment.Id,
            OrderId = shipment.OrderId,
            OrderCode = shipment.Order.Code,
            CustomerName = shipment.Order.CustomerName,
            CustomerPhone = shipment.Order.CustomerPhone,
            ShippingAddress = string.Join(", ", new[]
            {
                shipment.Order.ShippingAddressLine,
                shipment.Order.ShippingWard,
                shipment.Order.ShippingDistrict,
                shipment.Order.ShippingCity
            }.Where(value => !string.IsNullOrWhiteSpace(value))),
            DistrictId = shipment.Order.ShippingDistrictId,
            WardCode = shipment.Order.ShippingWardCode,
            Direction = shipment.Direction,
            Status = shipment.Status,
            ProviderStatus = shipment.ProviderStatus,
            FulfillmentStatus = shipment.Order.FulfillmentStatus,
            PaymentStatus = shipment.Order.PaymentStatus,
            ServiceCode = shipment.ServiceCode,
            ServiceName = shipment.ServiceName,
            ExternalOrderCode = shipment.ExternalOrderCode,
            TrackingCode = shipment.TrackingCode,
            Fee = shipment.Fee,
            CodAmount = shipment.CodAmount,
            WeightGram = shipment.WeightGram > 0 ? shipment.WeightGram : _options.DefaultWeightGram,
            LengthCm = shipment.LengthCm > 0 ? shipment.LengthCm : _options.DefaultLengthCm,
            WidthCm = shipment.WidthCm > 0 ? shipment.WidthCm : _options.DefaultWidthCm,
            HeightCm = shipment.HeightCm > 0 ? shipment.HeightCm : _options.DefaultHeightCm,
            EstimatedDeliveryAt = shipment.EstimatedDeliveryAt,
            ProviderUpdatedAt = shipment.ProviderUpdatedAt,
            LastSyncedAt = shipment.LastSyncedAt,
            CarrierHandoffAt = shipment.CarrierHandoffAt,
            DeliveredAt = shipment.DeliveredAt,
            ReturnDeadlineAt = shipment.DeliveredAt.HasValue
                ? ReturnPolicy.GetDeadlineUtc(shipment.DeliveredAt.Value)
                : null,
            CancelRequestedAt = shipment.CancelRequestedAt,
            CancelledAt = shipment.CancelledAt,
            CanQueueCreate = string.IsNullOrWhiteSpace(shipment.ExternalOrderCode)
                && shipment.Status is ShipmentStatus.Draft or ShipmentStatus.Exception,
            HasPendingOrderCancellation = hasPendingOrderCancellation,
            CanRequestCancellation = hasPendingOrderCancellation
                && !shipment.CarrierHandoffAt.HasValue
                && (shipment.Status is ShipmentStatus.Draft
                    or ShipmentStatus.PendingCreation
                    or ShipmentStatus.Created),
            ShipperName = shipment.ShipperName,
            ShipperPhone = shipment.ShipperPhone,
            CurrentHub = shipment.CurrentHub,
            ProviderReason = shipment.ProviderReason,
            Outbox = outbox,
            Inbox = inbox
        };

        ViewBag.GhnEnabled = _options.Enabled;
        ViewBag.DefaultServiceTypeId = _options.DefaultServiceTypeId;
        return View(model);
    }

    [HttpGet("{id:long}/services")]
    public async Task<IActionResult> Services(long id, CancellationToken cancellationToken)
    {
        var result = await _shipping.GetServicesAsync(id, cancellationToken);
        return ProviderResponse(result);
    }

    [HttpPost("{id:long}/quote")]
    public async Task<IActionResult> Quote(
        long id,
        [FromBody] ShippingExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new { success = false, message = "Thông số kiện hàng không hợp lệ." });
        }

        var result = await _shipping.QuoteAsync(id, ToInput(request), cancellationToken);
        return ProviderResponse(result);
    }

    [HttpPost("{id:long}/create")]
    public async Task<IActionResult> QueueCreate(
        long id,
        [FromBody] ShippingExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new { success = false, message = "Thông số tạo vận đơn không hợp lệ." });
        }

        try
        {
            var result = await _shipping.QueueCreateAsync(
                id,
                ToInput(request),
                ResolveActor(),
                cancellationToken);
            return Ok(new { success = true, data = result, message = result.Message });
        }
        catch (CommerceFlowException exception)
        {
            return FlowError(exception);
        }
        catch (InvalidOperationException exception)
        {
            return UnprocessableEntity(new
            {
                success = false,
                errorCode = "SHIPPING_OPERATION_REJECTED",
                message = exception.Message
            });
        }
    }

    [HttpPost("{id:long}/cancel")]
    public async Task<IActionResult> QueueCancel(
        long id,
        [FromBody] ShippingCancelRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new { success = false, message = "Lý do hủy không hợp lệ." });
        }

        try
        {
            var result = await _shipping.QueueCancelAsync(
                id,
                request.Reason,
                ResolveActor(),
                cancellationToken);
            return Ok(new { success = true, data = result, message = result.Message });
        }
        catch (CommerceFlowException exception)
        {
            return FlowError(exception);
        }
        catch (InvalidOperationException exception)
        {
            return UnprocessableEntity(new
            {
                success = false,
                errorCode = "SHIPPING_OPERATION_REJECTED",
                message = exception.Message
            });
        }
    }

    [HttpPost("{id:long}/sync")]
    public async Task<IActionResult> Sync(long id, CancellationToken cancellationToken)
    {
        var result = await _shipping.SyncAsync(id, ResolveActor(), cancellationToken);
        return ProviderResponse(result);
    }


    private IActionResult FlowError(CommerceFlowException exception)
    {
        _logger.LogWarning(
            exception,
            "Shipping flow rejected. ErrorCode={ErrorCode}; Flow={FlowName}; Stage={Stage}; Aggregate={AggregateType}:{AggregateId}; CorrelationId={CorrelationId}",
            exception.ErrorCode,
            exception.FlowContext.FlowName,
            exception.FlowContext.Stage,
            exception.FlowContext.AggregateType,
            exception.FlowContext.AggregateId,
            exception.FlowContext.CorrelationId);

        return UnprocessableEntity(new
        {
            success = false,
            errorCode = exception.ErrorCode,
            flowName = exception.FlowContext.FlowName,
            stage = exception.FlowContext.Stage.ToString(),
            aggregateType = exception.FlowContext.AggregateType,
            aggregateId = exception.FlowContext.AggregateId,
            correlationId = exception.FlowContext.CorrelationId,
            message = exception.Message
        });
    }

    private IActionResult ProviderResponse<T>(ShippingOperationResult<T> result)
    {
        if (result.Success)
        {
            return Ok(new { success = true, data = result.Data });
        }

        _logger.LogWarning(
            "Shipping provider operation failed with {ErrorCode}: {Message}",
            result.ErrorCode,
            result.Message);

        return StatusCode(
            result.Retryable
                ? StatusCodes.Status503ServiceUnavailable
                : StatusCodes.Status422UnprocessableEntity,
            new
            {
                success = false,
                errorCode = result.ErrorCode,
                message = result.Message,
                retryable = result.Retryable
            });
    }

    private static ShippingExecutionInput ToInput(ShippingExecutionRequest request) =>
        new(
            request.ServiceId,
            request.ServiceTypeId,
            request.WeightGram,
            request.LengthCm,
            request.WidthCm,
            request.HeightCm,
            request.Note);

    private string ResolveActor()
    {
        var name = User.Identity?.Name;
        return string.IsNullOrWhiteSpace(name) ? "Admin" : name;
    }
}
