using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Areas.Admin.ViewModels.Shipping;
using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Shipping.Internal;

namespace WebApplication2.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/Shipping")]
public sealed class ShippingController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IInternalShippingLifecycleService _shipping;
    private readonly ILogger<ShippingController> _logger;

    public ShippingController(
        ApplicationDbContext context,
        IInternalShippingLifecycleService shipping,
        ILogger<ShippingController> logger)
    {
        _context = context;
        _shipping = shipping;
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
                || (item.TrackingCode != null
                    && item.TrackingCode.Contains(normalizedSearch)));
        }

        if (status.HasValue)
        {
            query = query.Where(item => item.Status == status.Value);
        }

        var items = await query
            .OrderByDescending(item => item.UpdatedAt ?? item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .Take(250)
            .Select(item => new ShippingAdminListItemViewModel
            {
                Id = item.Id,
                OrderId = item.OrderId,
                OrderCode = item.Order.Code,
                CustomerName = item.Order.CustomerName,
                Status = item.Status,
                TrackingCode = item.TrackingCode,
                ServiceName = string.IsNullOrWhiteSpace(item.ServiceName)
                    ? "Giao hàng nhanh"
                    : item.ServiceName,
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
    public async Task<IActionResult> Details(
        long id,
        CancellationToken cancellationToken)
    {
        var shipment = await _context.Shipments
            .AsNoTracking()
            .AsSplitQuery()
            .Include(item => item.Order)
                .ThenInclude(order => order.StatusHistory)
            .SingleOrDefaultAsync(
                item => item.Id == id
                    && item.Direction == ShipmentDirection.Outbound,
                cancellationToken);

        if (shipment is null)
        {
            return NotFound();
        }

        return View(new ShippingAdminDetailsViewModel
        {
            Id = shipment.Id,
            OrderId = shipment.OrderId,
            OrderCode = shipment.Order.Code,
            CustomerName = shipment.Order.CustomerName,
            CustomerPhone = shipment.Order.CustomerPhone,
            ShippingAddress = BuildAddress(shipment.Order),
            Status = shipment.Status,
            FulfillmentStatus = shipment.Order.FulfillmentStatus,
            PaymentStatus = shipment.Order.PaymentStatus,
            TrackingCode = shipment.TrackingCode,
            ServiceName = string.IsNullOrWhiteSpace(shipment.ServiceName)
                ? "Giao hàng nhanh"
                : shipment.ServiceName,
            Fee = shipment.Fee,
            CodAmount = shipment.CodAmount,
            CreatedAt = shipment.CreatedAt,
            UpdatedAt = shipment.UpdatedAt,
            EstimatedDeliveryAt = shipment.EstimatedDeliveryAt,
            CarrierHandoffAt = shipment.CarrierHandoffAt,
            DeliveredAt = shipment.DeliveredAt,
            Note = shipment.ProviderReason,
            RowVersion = Convert.ToBase64String(shipment.RowVersion),
            AllowedTargets = _shipping.GetAllowedTargets(shipment.Status),
            Progress = BuildProgress(shipment.Status),
            History = shipment.Order.StatusHistory
                .Where(item =>
                    item.Category == OrderHistoryCategory.Fulfillment
                    && item.CustomerVisible)
                .OrderByDescending(item => item.OccurredAt)
                .Select(item => new ShippingHistoryItemViewModel
                {
                    Title = item.Title,
                    Description = item.Description,
                    ChangedBy = item.ChangedBy,
                    OccurredAt = item.OccurredAt
                })
                .ToArray()
        });
    }

    [HttpPost("{id:long}/transition")]
    public async Task<IActionResult> Transition(
        long id,
        ShippingTransitionInput input,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] =
                "Thông tin cập nhật giao hàng chưa hợp lệ.";
            return RedirectToAction(nameof(Details), new { id });
        }

        try
        {
            var result = await _shipping.TransitionAsync(
                id,
                input.TargetStatus,
                DecodeRowVersion(input.RowVersion),
                ResolveActor(),
                input.Note,
                cancellationToken);

            TempData["SuccessMessage"] = result.Message;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            _logger.LogWarning(
                exception,
                "Concurrent internal shipping update for shipment {ShipmentId}.",
                id);
            TempData["ErrorMessage"] =
                "Dữ liệu vừa được cập nhật ở nơi khác. Vui lòng tải lại trang.";
        }
        catch (InvalidOperationException exception)
        {
            TempData["ErrorMessage"] = exception.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    private static byte[] DecodeRowVersion(string value)
    {
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return [];
        }
    }

    private static IReadOnlyList<ShippingProgressStepViewModel> BuildProgress(
        ShipmentStatus status)
    {
        var current = status switch
        {
            ShipmentStatus.Draft or ShipmentStatus.PendingCreation => 1,
            ShipmentStatus.Picking => 2,
            ShipmentStatus.Created => 3,
            ShipmentStatus.InTransit or ShipmentStatus.DeliveryFailed => 4,
            ShipmentStatus.Delivered => 5,
            ShipmentStatus.Returning or ShipmentStatus.Returned => 4,
            ShipmentStatus.Cancelled => 1,
            _ => 1
        };

        var steps = new[]
        {
            ("Đã ghi nhận", "Đơn hàng đã sẵn sàng để xử lý."),
            ("Chuẩn bị hàng", "Kiểm tra, đóng gói và xác nhận sản phẩm."),
            ("Sẵn sàng giao", "Đơn đã sẵn sàng bàn giao."),
            ("Đang giao", "Đơn đang trên đường đến khách hàng."),
            ("Hoàn tất", "Khách hàng đã nhận được đơn.")
        };

        return steps.Select((step, index) =>
            new ShippingProgressStepViewModel
            {
                Position = index + 1,
                Title = step.Item1,
                Description = step.Item2,
                IsComplete = index + 1 <= current,
                IsCurrent = index + 1 == current
            }).ToArray();
    }

    private string ResolveActor() =>
        string.IsNullOrWhiteSpace(User.Identity?.Name)
            ? "Admin"
            : User.Identity.Name;

    private static string BuildAddress(Order order) =>
        string.Join(", ", new[]
        {
            order.ShippingAddressLine,
            order.ShippingWard,
            order.ShippingDistrict,
            order.ShippingCity
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
}
