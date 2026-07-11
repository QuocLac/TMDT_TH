using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Areas.Admin.ViewModels.Orders;
using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Commerce.Orders;

namespace WebApplication2.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/Orders")]
public sealed class OrdersController : Controller
{
    private const int PageSize = 20;

    private readonly ApplicationDbContext _context;
    private readonly IOrderWorkflowService _workflowService;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(
        ApplicationDbContext context,
        IOrderWorkflowService workflowService,
        ILogger<OrdersController> logger)
    {
        _context = context;
        _workflowService = workflowService;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        [FromQuery] OrderAdminIndexQuery request,
        CancellationToken cancellationToken)
    {
        var query = _context.Orders.AsNoTracking().AsQueryable();
        var normalizedSearch = request.Search?.Trim();
        var normalizedProvider = request.Provider?.Trim();

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            query = query.Where(order =>
                order.Code.Contains(normalizedSearch)
                || order.CustomerName.Contains(normalizedSearch)
                || order.CustomerPhone.Contains(normalizedSearch)
                || order.Shipments.Any(shipment =>
                    shipment.TrackingCode != null
                    && shipment.TrackingCode.Contains(normalizedSearch)));
        }

        if (request.OrderStatus.HasValue)
        {
            query = query.Where(order => order.OrderStatus == request.OrderStatus.Value);
        }

        if (request.PaymentStatus.HasValue)
        {
            query = query.Where(order => order.PaymentStatus == request.PaymentStatus.Value);
        }

        if (request.FulfillmentStatus.HasValue)
        {
            query = query.Where(order =>
                order.FulfillmentStatus == request.FulfillmentStatus.Value);
        }

        if (request.FromDate.HasValue)
        {
            var fromUtc = DateTime.SpecifyKind(request.FromDate.Value.Date, DateTimeKind.Utc);
            query = query.Where(order => order.CreatedAt >= fromUtc);
        }

        if (request.ToDate.HasValue)
        {
            var toExclusiveUtc = DateTime.SpecifyKind(
                request.ToDate.Value.Date.AddDays(1),
                DateTimeKind.Utc);
            query = query.Where(order => order.CreatedAt < toExclusiveUtc);
        }

        if (!string.IsNullOrWhiteSpace(normalizedProvider))
        {
            query = query.Where(order => order.Shipments.Any(shipment =>
                shipment.Provider == normalizedProvider));
        }

        var totalItems = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)PageSize));
        var page = Math.Clamp(request.Page, 1, totalPages);

        var rows = await query
            .OrderByDescending(order => order.CreatedAt)
            .ThenByDescending(order => order.Id)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(order => new
            {
                order.Id,
                order.Code,
                order.CustomerName,
                order.CustomerPhone,
                order.GrandTotal,
                order.Currency,
                order.OrderStatus,
                order.PaymentStatus,
                order.FulfillmentStatus,
                order.CreatedAt,
                order.PlacedAt,
                Provider = order.Shipments
                    .OrderByDescending(shipment => shipment.Id)
                    .Select(shipment => shipment.Provider)
                    .FirstOrDefault(),
                TrackingCode = order.Shipments
                    .OrderByDescending(shipment => shipment.Id)
                    .Select(shipment => shipment.TrackingCode)
                    .FirstOrDefault()
            })
            .ToArrayAsync(cancellationToken);

        var summary = await _context.Orders
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(group => new OrderAdminSummaryViewModel
            {
                TotalOrders = group.Count(),
                PendingConfirmation = group.Count(order =>
                    order.OrderStatus == OrderStatus.Placed),
                Processing = group.Count(order =>
                    order.OrderStatus == OrderStatus.Processing),
                ReadyToShip = group.Count(order =>
                    order.FulfillmentStatus == FulfillmentStatus.ReadyToShip),
                DeliveryFailed = group.Count(order =>
                    order.FulfillmentStatus == FulfillmentStatus.DeliveryFailed)
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? new OrderAdminSummaryViewModel();

        var providers = await _context.Shipments
            .AsNoTracking()
            .Select(shipment => shipment.Provider)
            .Where(provider => provider != string.Empty)
            .Distinct()
            .OrderBy(provider => provider)
            .ToArrayAsync(cancellationToken);

        var model = new OrderAdminIndexPageViewModel
        {
            Items = rows.Select(row => new OrderAdminListItemViewModel
            {
                Id = row.Id,
                Code = row.Code,
                CustomerName = row.CustomerName,
                CustomerPhone = row.CustomerPhone,
                GrandTotal = row.GrandTotal,
                Currency = row.Currency,
                OrderStatus = row.OrderStatus,
                PaymentStatus = row.PaymentStatus,
                FulfillmentStatus = row.FulfillmentStatus,
                Provider = row.Provider,
                TrackingCode = row.TrackingCode,
                CreatedAt = row.CreatedAt,
                PlacedAt = row.PlacedAt
            }).ToArray(),
            Summary = summary,
            Providers = providers,
            Search = normalizedSearch,
            OrderStatus = request.OrderStatus,
            PaymentStatus = request.PaymentStatus,
            FulfillmentStatus = request.FulfillmentStatus,
            FromDate = request.FromDate,
            ToDate = request.ToDate,
            Provider = normalizedProvider,
            Page = page,
            PageSize = PageSize,
            TotalItems = totalItems,
            TotalPages = totalPages
        };

        return View(model);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(
        int id,
        CancellationToken cancellationToken)
    {
        var order = await _context.Orders
            .AsNoTracking()
            .AsSplitQuery()
            .Include(item => item.Items)
            .Include(item => item.PaymentTransactions)
            .Include(item => item.Shipments)
            .Include(item => item.StockReservations)
            .Include(item => item.StatusHistory)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (order is null)
        {
            return NotFound();
        }

        var aggregateId = id.ToString(CultureInfo.InvariantCulture);
        var outbox = await _context.IntegrationOutboxMessages
            .AsNoTracking()
            .Where(message =>
                message.AggregateType == "Order"
                && message.AggregateId == aggregateId)
            .OrderByDescending(message => message.CreatedAt)
            .Select(message => new OrderAdminIntegrationViewModel
            {
                Direction = "Outbox",
                Provider = message.Provider,
                Type = message.MessageType,
                Status = message.Status.ToString(),
                AttemptCount = message.AttemptCount,
                LastError = message.LastError,
                CreatedAt = message.CreatedAt,
                CompletedAt = message.CompletedAt
            })
            .ToArrayAsync(cancellationToken);

        var inbox = await _context.IntegrationInboxEvents
            .AsNoTracking()
            .Where(message => message.CorrelationId == order.ClientRequestId)
            .OrderByDescending(message => message.ReceivedAt)
            .Select(message => new OrderAdminIntegrationViewModel
            {
                Direction = "Inbox",
                Provider = message.Provider,
                Type = message.EventType,
                Status = message.Status.ToString(),
                AttemptCount = message.AttemptCount,
                LastError = message.LastError,
                CreatedAt = message.ReceivedAt,
                CompletedAt = message.ProcessedAt
            })
            .ToArrayAsync(cancellationToken);

        var skuByItemId = order.Items.ToDictionary(item => item.Id, item => item.Sku);
        var model = new OrderAdminDetailsViewModel
        {
            Id = order.Id,
            Code = order.Code,
            PublicToken = order.PublicToken,
            ClientRequestId = order.ClientRequestId,
            CustomerName = order.CustomerName,
            CustomerEmail = order.CustomerEmail,
            CustomerPhone = order.CustomerPhone,
            ShippingAddress = string.Join(", ", new[]
            {
                order.ShippingAddressLine,
                order.ShippingWard,
                order.ShippingDistrict,
                order.ShippingCity
            }.Where(value => !string.IsNullOrWhiteSpace(value))),
            Subtotal = order.Subtotal,
            ShippingFee = order.ShippingFee,
            DiscountTotal = order.DiscountTotal,
            TaxTotal = order.TaxTotal,
            GrandTotal = order.GrandTotal,
            Currency = order.Currency,
            OrderStatus = order.OrderStatus,
            PaymentStatus = order.PaymentStatus,
            FulfillmentStatus = order.FulfillmentStatus,
            CreatedAt = order.CreatedAt,
            PlacedAt = order.PlacedAt,
            ConfirmedAt = order.ConfirmedAt,
            CompletedAt = order.CompletedAt,
            CancelledAt = order.CancelledAt,
            CancelReason = order.CancelReason,
            RowVersion = Convert.ToBase64String(order.RowVersion),
            AllowedOrderTransitions = _workflowService.GetAllowedOrderTransitions(
                order.OrderStatus),
            AllowedPaymentTransitions = _workflowService.GetAllowedPaymentTransitions(
                order.PaymentStatus),
            AllowedFulfillmentTransitions =
                _workflowService.GetAllowedFulfillmentTransitions(order.FulfillmentStatus),
            Items = order.Items
                .OrderBy(item => item.Id)
                .Select(item => new OrderAdminItemViewModel
                {
                    Id = item.Id,
                    ProductName = item.ProductName,
                    Sku = item.Sku,
                    VariantDescription = item.VariantDescription,
                    ImageUrl = item.ImageUrl,
                    ListPrice = item.ListPrice,
                    UnitPrice = item.UnitPrice,
                    DiscountAmount = item.DiscountAmount,
                    TaxAmount = item.TaxAmount,
                    Quantity = item.Quantity,
                    LineTotal = item.LineTotal
                })
                .ToArray(),
            Payments = order.PaymentTransactions
                .OrderByDescending(item => item.AttemptNumber)
                .ThenByDescending(item => item.Id)
                .Select(item => new OrderAdminPaymentViewModel
                {
                    Id = item.Id,
                    Provider = item.Provider,
                    Method = item.Method,
                    Status = item.Status,
                    AttemptNumber = item.AttemptNumber,
                    Amount = item.Amount,
                    Currency = item.Currency,
                    MerchantReference = item.MerchantReference,
                    ProviderTransactionId = item.ProviderTransactionId,
                    FailureCode = item.FailureCode,
                    FailureMessage = item.FailureMessage,
                    CreatedAt = item.CreatedAt,
                    CompletedAt = item.CompletedAt
                })
                .ToArray(),
            Shipments = order.Shipments
                .OrderByDescending(item => item.Id)
                .Select(item => new OrderAdminShipmentViewModel
                {
                    Id = item.Id,
                    Provider = item.Provider,
                    Status = item.Status,
                    ServiceCode = item.ServiceCode,
                    ServiceName = item.ServiceName,
                    ExternalOrderCode = item.ExternalOrderCode,
                    TrackingCode = item.TrackingCode,
                    Fee = item.Fee,
                    CodAmount = item.CodAmount,
                    EstimatedDeliveryAt = item.EstimatedDeliveryAt,
                    ShipperName = item.ShipperName,
                    ShipperPhone = item.ShipperPhone,
                    CurrentHub = item.CurrentHub,
                    ProviderReason = item.ProviderReason,
                    CreatedAt = item.CreatedAt
                })
                .ToArray(),
            Reservations = order.StockReservations
                .OrderBy(item => item.Id)
                .Select(item => new OrderAdminReservationViewModel
                {
                    Id = item.Id,
                    OrderItemId = item.OrderItemId,
                    Sku = skuByItemId.GetValueOrDefault(item.OrderItemId, $"Item #{item.OrderItemId}"),
                    Quantity = item.Quantity,
                    Status = item.Status,
                    ReservedAt = item.ReservedAt,
                    ExpiresAt = item.ExpiresAt,
                    CommittedAt = item.CommittedAt,
                    ReleasedAt = item.ReleasedAt,
                    ReleaseReason = item.ReleaseReason
                })
                .ToArray(),
            Timeline = order.StatusHistory
                .OrderByDescending(item => item.OccurredAt)
                .ThenByDescending(item => item.Id)
                .Select(item => new OrderAdminTimelineViewModel
                {
                    Id = item.Id,
                    Category = item.Category,
                    FromStatus = item.FromStatus,
                    ToStatus = item.ToStatus,
                    Code = item.Code,
                    Title = item.Title,
                    Description = item.Description,
                    ChangedBy = item.ChangedBy,
                    CustomerVisible = item.CustomerVisible,
                    OccurredAt = item.OccurredAt,
                    CorrelationId = item.CorrelationId
                })
                .ToArray(),
            IntegrationMessages = outbox
                .Concat(inbox)
                .OrderByDescending(item => item.CreatedAt)
                .ToArray()
        };

        return View(model);
    }

    [HttpPost("{id:int}/order-status")]
    public async Task<IActionResult> ChangeOrderStatus(
        int id,
        ChangeOrderStatusRequest request,
        CancellationToken cancellationToken)
    {
        return await ExecuteTransitionAsync(
            id,
            request.RowVersion,
            request.Reason,
            rowVersion => _workflowService.TransitionAsync(
                id,
                request.TargetStatus,
                rowVersion,
                ResolveActor(),
                request.Reason,
                cancellationToken));
    }

    [HttpPost("{id:int}/payment-status")]
    public async Task<IActionResult> ChangePaymentStatus(
        int id,
        ChangePaymentStatusRequest request,
        CancellationToken cancellationToken)
    {
        return await ExecuteTransitionAsync(
            id,
            request.RowVersion,
            request.Reason,
            rowVersion => _workflowService.TransitionPaymentAsync(
                id,
                request.TargetStatus,
                rowVersion,
                ResolveActor(),
                request.Reason,
                cancellationToken));
    }

    [HttpPost("{id:int}/fulfillment-status")]
    public async Task<IActionResult> ChangeFulfillmentStatus(
        int id,
        ChangeFulfillmentStatusRequest request,
        CancellationToken cancellationToken)
    {
        return await ExecuteTransitionAsync(
            id,
            request.RowVersion,
            request.Reason,
            rowVersion => _workflowService.TransitionFulfillmentAsync(
                id,
                request.TargetStatus,
                rowVersion,
                ResolveActor(),
                request.Reason,
                cancellationToken));
    }

    private async Task<IActionResult> ExecuteTransitionAsync(
        int orderId,
        string rowVersionValue,
        string reason,
        Func<byte[], Task<Order>> operation)
    {
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(reason))
        {
            TempData["ErrorMessage"] = "Trạng thái, RowVersion và lý do thao tác là bắt buộc.";
            return RedirectToAction(nameof(Details), new { id = orderId });
        }

        byte[] rowVersion;
        try
        {
            rowVersion = Convert.FromBase64String(rowVersionValue);
        }
        catch (FormatException)
        {
            TempData["ErrorMessage"] = "RowVersion không hợp lệ. Vui lòng tải lại đơn hàng.";
            return RedirectToAction(nameof(Details), new { id = orderId });
        }

        try
        {
            await operation(rowVersion);
            TempData["SuccessMessage"] = "Đã cập nhật trạng thái và ghi vào timeline đơn hàng.";
        }
        catch (OrderConcurrencyException exception)
        {
            _logger.LogWarning(
                exception,
                "Order {OrderId} transition rejected because RowVersion was stale.",
                orderId);
            TempData["ErrorMessage"] = exception.Message;
        }
        catch (OrderTransitionException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
        }

        return RedirectToAction(nameof(Details), new { id = orderId });
    }

    private string ResolveActor()
    {
        var name = User.Identity?.Name;
        return string.IsNullOrWhiteSpace(name) ? "Admin" : name;
    }
}
