using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Commerce.Cancellations;
using WebApplication2.Services.Commerce.Returns;
using WebApplication2.Services.Identity;
using WebApplication2.ViewModels.Storefront.Orders;

namespace WebApplication2.Controllers;

[Authorize]
[Route("orders")]
public sealed class OrdersController : Controller
{
    private const int PageSize = 8;

    private readonly ApplicationDbContext _context;
    private readonly IOrderCancellationService _cancellationService;
    private readonly IReturnWorkflowService _returnWorkflow;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(
        ApplicationDbContext context,
        IOrderCancellationService cancellationService,
        IReturnWorkflowService returnWorkflow,
        ILogger<OrdersController> logger)
    {
        _context = context;
        _cancellationService = cancellationService;
        _returnWorkflow = returnWorkflow;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? status = null,
        string? query = null,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var customerId = RequireCustomerId();
        var normalizedStatus = NormalizeStatus(status);
        var normalizedQuery = query?.Trim() ?? string.Empty;
        page = Math.Max(1, page);

        var customerOrders = _context.Orders
            .AsNoTracking()
            .Where(item => item.CustomerId == customerId);

        var summaryRows = await customerOrders
            .Select(item => new
            {
                item.OrderStatus,
                item.PaymentStatus,
                item.FulfillmentStatus,
                HasReturn = item.ReturnRequests.Any()
            })
            .ToArrayAsync(cancellationToken);

        var filtered = ApplyStatusFilter(customerOrders, normalizedStatus);
        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            filtered = filtered.Where(item =>
                item.Code.Contains(normalizedQuery)
                || item.Items.Any(line =>
                    line.ProductName.Contains(normalizedQuery)
                    || line.Sku.Contains(normalizedQuery)));
        }

        var totalCount = await filtered.CountAsync(cancellationToken);
        var totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)PageSize);
        if (totalPages > 0 && page > totalPages)
        {
            page = totalPages;
        }

        var orders = await filtered
            .AsSplitQuery()
            .Include(item => item.Items)
            .Include(item => item.Shipments)
            .Include(item => item.PaymentTransactions)
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .ToArrayAsync(cancellationToken);

        var summary = new CustomerOrderSummaryViewModel
        {
            All = summaryRows.Length,
            AwaitingPayment = summaryRows.Count(item =>
                item.OrderStatus == OrderStatus.PendingPayment
                && item.PaymentStatus == PaymentStatus.Pending),
            Processing = summaryRows.Count(item =>
                item.OrderStatus != OrderStatus.PendingPayment
                && item.OrderStatus != OrderStatus.Cancelled
                && item.OrderStatus != OrderStatus.Completed
                && item.OrderStatus != OrderStatus.Closed
                && item.FulfillmentStatus != FulfillmentStatus.Shipped
                && item.FulfillmentStatus != FulfillmentStatus.Delivered
                && item.FulfillmentStatus != FulfillmentStatus.Returning
                && item.FulfillmentStatus != FulfillmentStatus.Returned),
            Shipping = summaryRows.Count(item =>
                item.FulfillmentStatus == FulfillmentStatus.Shipped
                || item.FulfillmentStatus == FulfillmentStatus.DeliveryFailed),
            Completed = summaryRows.Count(item =>
                item.OrderStatus == OrderStatus.Completed
                || item.OrderStatus == OrderStatus.Closed
                || item.FulfillmentStatus == FulfillmentStatus.Delivered),
            Cancelled = summaryRows.Count(item =>
                item.OrderStatus == OrderStatus.Cancelled),
            Returns = summaryRows.Count(item => item.HasReturn)
        };

        return View(new CustomerOrderIndexViewModel
        {
            Query = normalizedQuery,
            Status = normalizedStatus,
            Page = page,
            TotalCount = totalCount,
            TotalPages = totalPages,
            Summary = summary,
            Orders = orders.Select(ToOrderCard).ToArray()
        });
    }

    [HttpGet("{publicToken:guid}")]
    public async Task<IActionResult> Details(
        Guid publicToken,
        CancellationToken cancellationToken)
    {
        var model = await BuildDetailsAsync(
            publicToken,
            cancellationForm: null,
            errorMessage: null,
            cancellationToken);

        return model is null ? NotFound() : View(model);
    }

    [HttpPost("{publicToken:guid}/cancel")]
    public async Task<IActionResult> RequestCancellation(
        Guid publicToken,
        [Bind(Prefix = "Cancellation")] CustomerCancellationInputModel input,
        CancellationToken cancellationToken)
    {
        var customerId = RequireCustomerId();
        var ownsOrder = input.OrderPublicToken == publicToken
            && await _context.Orders.AsNoTracking().AnyAsync(
                item => item.Id == input.OrderId
                    && item.PublicToken == publicToken
                    && item.CustomerId == customerId,
                cancellationToken);

        if (!ownsOrder)
        {
            return NotFound();
        }

        OrderCancellationSummary? cancellationSummary = null;
        try
        {
            cancellationSummary = await _cancellationService.GetSummaryAsync(
                input.OrderId,
                cancellationToken);
        }
        catch (OrderCancellationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.DetailMessage);
        }

        var canCancelWholeOrder = cancellationSummary is not null
            && cancellationSummary.CanRequest
            && cancellationSummary.Items.Count > 0
            && cancellationSummary.Items.All(item =>
                item.OrderedQuantity > 0
                && item.ApprovedCancelledQuantity == 0
                && item.PendingQuantity == 0
                && item.CancellableQuantity == item.OrderedQuantity);

        if (!canCancelWholeOrder)
        {
            ModelState.AddModelError(
                string.Empty,
                "FastBuy chỉ hỗ trợ hủy toàn bộ đơn hàng. Đơn có sản phẩm đã hủy, đang chờ hủy hoặc không còn đủ số lượng sẽ không thể tạo yêu cầu mới.");
        }

        var wholeOrderLines = cancellationSummary?.Items
            .OrderBy(item => item.OrderItemId)
            .Select(item => new CancellationRequestLineCommand(
                item.OrderItemId,
                item.OrderedQuantity))
            .ToArray()
            ?? [];

        if (!TryDecodeRowVersion(input.OrderRowVersion, out var rowVersion))
        {
            ModelState.AddModelError(
                string.Empty,
                "Dữ liệu đơn hàng đã hết hiệu lực. Vui lòng tải lại trang.");
        }

        if (!ModelState.IsValid)
        {
            return await RenderDetailsAsync(
                publicToken,
                input,
                FirstModelError(),
                cancellationToken);
        }

        try
        {
            await _cancellationService.RequestAsync(
                new CreateOrderCancellationCommand(
                    input.OrderId,
                    rowVersion,
                    input.ReasonCode,
                    input.ReasonText,
                    ResolveActor(customerId),
                    input.IdempotencyKey,
                    wholeOrderLines),
                cancellationToken);

            TempData["SuccessMessage"] =
                "Yêu cầu hủy đã được ghi nhận. Bạn có thể theo dõi trạng thái ngay trong chi tiết đơn.";
        }
        catch (OrderCancellationException exception)
        {
            _logger.LogWarning(
                exception,
                "Customer cancellation failed for order {OrderId}. ErrorCode={ErrorCode}",
                input.OrderId,
                exception.ErrorCode);

            return await RenderDetailsAsync(
                publicToken,
                input,
                exception.DetailMessage,
                cancellationToken);
        }

        return RedirectToAction(nameof(Details), new { publicToken });
    }

    private async Task<IActionResult> RenderDetailsAsync(
        Guid publicToken,
        CustomerCancellationInputModel input,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        var model = await BuildDetailsAsync(
            publicToken,
            input,
            errorMessage,
            cancellationToken);

        return model is null ? NotFound() : View("Details", model);
    }

    private async Task<CustomerOrderDetailsViewModel?> BuildDetailsAsync(
        Guid publicToken,
        CustomerCancellationInputModel? cancellationForm,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        var customerId = RequireCustomerId();
        var order = await _context.Orders
            .AsNoTracking()
            .AsSplitQuery()
            .Include(item => item.Items)
            .Include(item => item.PaymentTransactions)
            .Include(item => item.Shipments)
            .Include(item => item.StatusHistory)
            .Include(item => item.ReturnRequests)
                .ThenInclude(item => item.Items)
                    .ThenInclude(item => item.OrderItem)
            .Include(item => item.ReturnRequests)
                .ThenInclude(item => item.Shipments)
            .SingleOrDefaultAsync(
                item => item.PublicToken == publicToken
                    && item.CustomerId == customerId,
                cancellationToken);

        if (order is null)
        {
            return null;
        }

        OrderCancellationSummary? cancellation = null;
        string? cancellationLoadError = null;
        try
        {
            cancellation = await _cancellationService.GetSummaryAsync(
                order.Id,
                cancellationToken);
        }
        catch (OrderCancellationException exception)
        {
            cancellationLoadError = exception.DetailMessage;
            _logger.LogWarning(
                exception,
                "Unable to load cancellation summary for order {OrderId}.",
                order.Id);
        }

        var returnEligibility = await _returnWorkflow.GetEligibilityAsync(
            order.PublicToken,
            cancellationToken);

        var cancellableByItem = cancellation?.Items.ToDictionary(
            item => item.OrderItemId,
            item => item.CancellableQuantity)
            ?? new Dictionary<int, int>();

        var latestPayment = order.PaymentTransactions
            .Where(item => !item.Method.Equals(
                "Refund",
                StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.AttemptNumber)
            .ThenByDescending(item => item.Id)
            .FirstOrDefault();
        var shipment = order.Shipments
            .Where(item => item.Direction == ShipmentDirection.Outbound)
            .OrderByDescending(item => item.Id)
            .FirstOrDefault();
        var status = ResolveStatus(order);

        return new CustomerOrderDetailsViewModel
        {
            OrderId = order.Id,
            PublicToken = order.PublicToken,
            Code = order.Code,
            CreatedAt = order.CreatedAt,
            OrderStatus = order.OrderStatus,
            PaymentStatus = order.PaymentStatus,
            FulfillmentStatus = order.FulfillmentStatus,
            StatusText = status.Text,
            StatusTone = status.Tone,
            PaymentStatusText = CustomerOrderDisplay.PaymentStatusText(order.PaymentStatus),
            FulfillmentStatusText = CustomerOrderDisplay.FulfillmentStatusText(order.FulfillmentStatus),
            CustomerName = order.CustomerName,
            CustomerEmail = order.CustomerEmail,
            CustomerPhone = order.CustomerPhone,
            ShippingAddress = BuildShippingAddress(order),
            Subtotal = order.Subtotal,
            ShippingFee = order.ShippingFee,
            DiscountTotal = order.DiscountTotal,
            TaxTotal = order.TaxTotal,
            GrandTotal = order.GrandTotal,
            PaymentMethodText = CustomerOrderDisplay.PaymentMethod(latestPayment?.Method),
            CanRetryPayment = order.OrderStatus == OrderStatus.PendingPayment
                && order.PaymentStatus == PaymentStatus.Pending
                && latestPayment?.Method.Equals(
                    "VNPAY",
                    StringComparison.OrdinalIgnoreCase) == true,
            CanRequestCancellation = cancellation?.CanRequest == true
                && cancellation!.Items.Count > 0
                && cancellation.Items.All(item =>
                    item.OrderedQuantity > 0
                    && item.ApprovedCancelledQuantity == 0
                    && item.PendingQuantity == 0
                    && item.CancellableQuantity == item.OrderedQuantity),
            CancellationMessage = cancellation?.CanRequest == true
                && cancellation!.Items.Any(item =>
                    item.ApprovedCancelledQuantity > 0
                    || item.PendingQuantity > 0
                    || item.CancellableQuantity != item.OrderedQuantity)
                    ? "FastBuy chỉ hỗ trợ hủy toàn bộ đơn hàng. Đơn này đã có số lượng được hủy, đang chờ hủy hoặc không còn nguyên vẹn nên không thể tạo yêu cầu hủy mới."
                    : cancellation?.IneligibilityMessage
                        ?? cancellationLoadError,
            CanRequestReturn = returnEligibility?.IsEligible == true,
            ReturnMessage = returnEligibility?.Message,
            CancelReason = order.CancelReason,
            ErrorMessage = errorMessage,
            Shipment = shipment is null ? null : new CustomerShipmentViewModel
            {
                Provider = shipment.Provider,
                StatusText = CustomerOrderDisplay.ShipmentStatusText(shipment.Status),
                StatusTone = CustomerOrderDisplay.Tone(shipment.Status),
                ServiceName = shipment.ServiceName,
                TrackingCode = shipment.TrackingCode,
                EstimatedDeliveryAt = shipment.EstimatedDeliveryAt,
                CurrentHub = shipment.CurrentHub,
                ProviderReason = shipment.ProviderReason
            },
            Cancellation = new CustomerCancellationInputModel
            {
                OrderId = order.Id,
                OrderPublicToken = order.PublicToken,
                OrderRowVersion = cancellation?.OrderRowVersion
                    ?? Convert.ToBase64String(order.RowVersion),
                ReasonCode = cancellationForm?.ReasonCode ?? string.Empty,
                ReasonText = cancellationForm?.ReasonText ?? string.Empty,
                IdempotencyKey = string.IsNullOrWhiteSpace(cancellationForm?.IdempotencyKey)
                    ? Guid.NewGuid().ToString("N")
                    : cancellationForm.IdempotencyKey,
                Lines = cancellation?.Items
                    .OrderBy(item => item.OrderItemId)
                    .Select(item => new CustomerCancellationLineInputModel
                    {
                        OrderItemId = item.OrderItemId,
                        Quantity = item.OrderedQuantity
                    })
                    .ToList()
                    ?? []
            },
            Progress = BuildProgress(order),
            Items = order.Items.OrderBy(item => item.Id).Select(item =>
                new CustomerOrderLineViewModel
                {
                    OrderItemId = item.Id,
                    ProductName = item.ProductName,
                    Sku = item.Sku,
                    VariantDescription = item.VariantDescription,
                    ImageUrl = item.ImageUrl,
                    UnitPrice = item.UnitPrice,
                    LineTotal = item.LineTotal,
                    Quantity = item.Quantity,
                    CancellableQuantity = cancellableByItem.GetValueOrDefault(item.Id)
                }).ToArray(),
            Timeline = order.StatusHistory
                .Where(item => item.CustomerVisible)
                .OrderByDescending(item => item.OccurredAt)
                .ThenByDescending(item => item.Id)
                .Select(item => new CustomerOrderTimelineViewModel
                {
                    CategoryText = CustomerOrderDisplay.Category(item.Category),
                    Title = item.Title,
                    Description = item.Description,
                    OccurredAt = item.OccurredAt
                }).ToArray(),
            Payments = order.PaymentTransactions
                .OrderByDescending(item => item.AttemptNumber)
                .ThenByDescending(item => item.Id)
                .Select(item => new CustomerPaymentAttemptViewModel
                {
                    Provider = item.Provider,
                    MethodText = CustomerOrderDisplay.PaymentMethod(item.Method),
                    StatusText = CustomerOrderDisplay.PaymentStatusText(item.Status),
                    StatusTone = CustomerOrderDisplay.Tone(item.Status),
                    AttemptNumber = item.AttemptNumber,
                    Amount = item.Amount,
                    CreatedAt = item.CreatedAt,
                    FailureMessage = item.FailureMessage
                }).ToArray(),
            CancellationRequests = cancellation?.Requests.Select(item =>
                new CustomerCancellationRequestViewModel
                {
                    Id = item.Id,
                    StatusText = CustomerOrderDisplay.CancellationStatus(item.Status),
                    StatusTone = CustomerOrderDisplay.CancellationTone(item.Status),
                    ReasonText = item.ReasonText,
                    RequestedAt = item.RequestedAt,
                    ReviewNote = item.ReviewNote,
                    RefundAmount = item.RefundAmount,
                    Items = item.Items.Select(line =>
                        new CustomerCancellationRequestLineViewModel
                        {
                            ProductName = line.ProductName,
                            Sku = line.Sku,
                            RequestedQuantity = line.RequestedQuantity,
                            ApprovedQuantity = line.ApprovedQuantity
                        }).ToArray()
                }).ToArray() ?? [],
            ReturnRequests = order.ReturnRequests
                .OrderByDescending(item => item.RequestedAt)
                .Select(item => new CustomerReturnRequestViewModel
                {
                    Code = item.Code,
                    StatusText = CustomerOrderDisplay.ReturnStatusText(item.Status),
                    StatusTone = CustomerOrderDisplay.Tone(item.Status),
                    ReasonText = item.ReasonText,
                    RequestedAt = item.RequestedAt,
                    RequestedQuantity = item.Items.Sum(line => line.RequestedQuantity),
                    RefundAmount = item.Items.Sum(line => line.RefundAmount),
                    TrackingCode = item.Shipments
                        .Where(line => line.Direction == ShipmentDirection.Return)
                        .OrderByDescending(line => line.Id)
                        .Select(line => line.TrackingCode)
                        .FirstOrDefault()
                }).ToArray()
        };
    }

    private static IQueryable<Order> ApplyStatusFilter(
        IQueryable<Order> source,
        string status) => status switch
    {
        "payment" => source.Where(item =>
            item.OrderStatus == OrderStatus.PendingPayment
            && item.PaymentStatus == PaymentStatus.Pending),
        "processing" => source.Where(item =>
            item.OrderStatus != OrderStatus.PendingPayment
            && item.OrderStatus != OrderStatus.Cancelled
            && item.OrderStatus != OrderStatus.Completed
            && item.OrderStatus != OrderStatus.Closed
            && item.FulfillmentStatus != FulfillmentStatus.Shipped
            && item.FulfillmentStatus != FulfillmentStatus.Delivered
            && item.FulfillmentStatus != FulfillmentStatus.Returning
            && item.FulfillmentStatus != FulfillmentStatus.Returned),
        "shipping" => source.Where(item =>
            item.FulfillmentStatus == FulfillmentStatus.Shipped
            || item.FulfillmentStatus == FulfillmentStatus.DeliveryFailed),
        "completed" => source.Where(item =>
            item.OrderStatus == OrderStatus.Completed
            || item.OrderStatus == OrderStatus.Closed
            || item.FulfillmentStatus == FulfillmentStatus.Delivered),
        "cancelled" => source.Where(item =>
            item.OrderStatus == OrderStatus.Cancelled),
        "returns" => source.Where(item => item.ReturnRequests.Any()),
        _ => source
    };

    private static CustomerOrderCardViewModel ToOrderCard(Order order)
    {
        var shipment = order.Shipments
            .Where(item => item.Direction == ShipmentDirection.Outbound)
            .OrderByDescending(item => item.Id)
            .FirstOrDefault();
        var latestPayment = order.PaymentTransactions
            .Where(item => !item.Method.Equals(
                "Refund",
                StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.AttemptNumber)
            .ThenByDescending(item => item.Id)
            .FirstOrDefault();
        var status = ResolveStatus(order);
        var preview = order.Items.OrderBy(item => item.Id).Take(2).Select(item =>
            new CustomerOrderCardLineViewModel
            {
                ProductName = item.ProductName,
                VariantDescription = item.VariantDescription,
                ImageUrl = item.ImageUrl,
                Quantity = item.Quantity
            }).ToArray();

        return new CustomerOrderCardViewModel
        {
            PublicToken = order.PublicToken,
            Code = order.Code,
            CreatedAt = order.CreatedAt,
            StatusText = status.Text,
            StatusTone = status.Tone,
            PaymentStatusText = CustomerOrderDisplay.PaymentStatusText(order.PaymentStatus),
            FulfillmentStatusText = CustomerOrderDisplay.FulfillmentStatusText(order.FulfillmentStatus),
            GrandTotal = order.GrandTotal,
            TotalQuantity = order.Items.Sum(item => item.Quantity),
            TrackingCode = shipment?.TrackingCode,
            CanRetryPayment = order.OrderStatus == OrderStatus.PendingPayment
                && order.PaymentStatus == PaymentStatus.Pending
                && latestPayment?.Method.Equals(
                    "VNPAY",
                    StringComparison.OrdinalIgnoreCase) == true,
            AdditionalItemCount = Math.Max(0, order.Items.Count - preview.Length),
            Items = preview
        };
    }

    private static IReadOnlyList<CustomerOrderProgressStepViewModel> BuildProgress(
        Order order)
    {
        if (order.OrderStatus == OrderStatus.Cancelled)
        {
            return
            [
                new()
                {
                    Position = 1,
                    Title = "Đã đặt hàng",
                    Description = "Đơn hàng đã được ghi nhận.",
                    IsComplete = true
                },
                new()
                {
                    Position = 2,
                    Title = "Đã hủy",
                    Description = order.CancelReason ?? "Đơn hàng đã được hủy.",
                    IsComplete = true,
                    IsCurrent = true
                }
            ];
        }

        var confirmed = order.OrderStatus == OrderStatus.Confirmed
            || order.OrderStatus == OrderStatus.Processing
            || order.OrderStatus == OrderStatus.Completed
            || order.OrderStatus == OrderStatus.Closed;
        var shipped = order.FulfillmentStatus == FulfillmentStatus.Shipped
            || order.FulfillmentStatus == FulfillmentStatus.Delivered
            || order.FulfillmentStatus == FulfillmentStatus.Returning
            || order.FulfillmentStatus == FulfillmentStatus.Returned;
        var delivered = order.FulfillmentStatus == FulfillmentStatus.Delivered
            || order.OrderStatus == OrderStatus.Completed
            || order.OrderStatus == OrderStatus.Closed;

        var raw = new[]
        {
            new CustomerOrderProgressStepViewModel
            {
                Position = 1,
                Title = "Đã đặt hàng",
                Description = "FastBuy đã ghi nhận đơn hàng.",
                IsComplete = true
            },
            new CustomerOrderProgressStepViewModel
            {
                Position = 2,
                Title = "Đã xác nhận",
                Description = "Đơn được xác nhận và chuẩn bị.",
                IsComplete = confirmed
            },
            new CustomerOrderProgressStepViewModel
            {
                Position = 3,
                Title = "Đang giao",
                Description = "Đơn vị vận chuyển đang giao hàng.",
                IsComplete = shipped
            },
            new CustomerOrderProgressStepViewModel
            {
                Position = 4,
                Title = "Hoàn tất",
                Description = "Đơn đã giao và hoàn tất.",
                IsComplete = delivered
            }
        };

        var current = Array.FindIndex(raw, item => !item.IsComplete);
        if (current < 0)
        {
            current = raw.Length - 1;
        }

        return raw.Select((item, index) => new CustomerOrderProgressStepViewModel
        {
            Position = item.Position,
            Title = item.Title,
            Description = item.Description,
            IsComplete = item.IsComplete,
            IsCurrent = index == current
        }).ToArray();
    }

    private static (string Text, string Tone) ResolveStatus(Order order)
    {
        if (order.OrderStatus == OrderStatus.Cancelled)
        {
            return ("Đã hủy", "danger");
        }

        if (order.FulfillmentStatus == FulfillmentStatus.Returning
            || order.FulfillmentStatus == FulfillmentStatus.Returned)
        {
            return (
                CustomerOrderDisplay.FulfillmentStatusText(order.FulfillmentStatus),
                "warning");
        }

        if (order.OrderStatus == OrderStatus.Completed
            || order.OrderStatus == OrderStatus.Closed
            || order.FulfillmentStatus == FulfillmentStatus.Delivered)
        {
            return ("Hoàn tất", "success");
        }

        if (order.FulfillmentStatus == FulfillmentStatus.Shipped
            || order.FulfillmentStatus == FulfillmentStatus.DeliveryFailed)
        {
            return (
                CustomerOrderDisplay.FulfillmentStatusText(order.FulfillmentStatus),
                CustomerOrderDisplay.Tone(order.FulfillmentStatus));
        }

        if (order.OrderStatus == OrderStatus.PendingPayment
            && order.PaymentStatus == PaymentStatus.Pending)
        {
            return ("Chờ thanh toán", "warning");
        }

        return (
            CustomerOrderDisplay.OrderStatusText(order.OrderStatus),
            CustomerOrderDisplay.Tone(order.OrderStatus));
    }

    private static string BuildShippingAddress(Order order) => string.Join(
        ", ",
        new[]
        {
            order.ShippingAddressLine,
            order.ShippingWard,
            order.ShippingDistrict,
            order.ShippingCity
        }.Where(item => !string.IsNullOrWhiteSpace(item)));

    private static bool TryDecodeRowVersion(
        string? value,
        out byte[] rowVersion)
    {
        rowVersion = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            rowVersion = Convert.FromBase64String(value);
            return rowVersion.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string NormalizeStatus(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "payment" => "payment",
            "processing" => "processing",
            "shipping" => "shipping",
            "completed" => "completed",
            "cancelled" => "cancelled",
            "returns" => "returns",
            _ => "all"
        };

    private int RequireCustomerId() => User.GetCustomerId()
        ?? throw new InvalidOperationException(
            "Authenticated account has no CustomerId claim.");

    private string ResolveActor(int customerId) => User.Identity?.Name
        ?? User.FindFirstValue(ClaimTypes.Email)
        ?? $"customer:{customerId}";

    private string FirstModelError() => ModelState.Values
        .SelectMany(item => item.Errors)
        .Select(item => item.ErrorMessage)
        .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item))
        ?? "Yêu cầu chưa hợp lệ.";
}
