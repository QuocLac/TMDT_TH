using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Areas.Admin.ViewModels.Home;
using WebApplication2.Models;
using WebApplication2.Models.Enums;

namespace WebApplication2.Areas.Admin.Controllers;

[Area("Admin")]
public sealed class HomeController : Controller
{
    private const int LowStockThreshold = 5;
    private const int DashboardItemLimit = 7;
    private const int MaximumDashboardRangeDays = 366;

    private static readonly TimeZoneInfo BusinessTimeZone =
        ResolveBusinessTimeZone();

    private static readonly ReturnRequestStatus[] OpenReturnStatuses =
    [
        ReturnRequestStatus.Requested,
        ReturnRequestStatus.UnderReview,
        ReturnRequestStatus.Approved,
        ReturnRequestStatus.AwaitingReturnShipment,
        ReturnRequestStatus.AwaitingPickup,
        ReturnRequestStatus.ReturnInTransit,
        ReturnRequestStatus.ReceivedAtWarehouse,
        ReturnRequestStatus.Inspecting,
        ReturnRequestStatus.RefundPending
    ];

    private readonly ApplicationDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public HomeController(
        ApplicationDbContext dbContext,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
    }

    public async Task<IActionResult> Index(
        string? preset,
        DateTime? fromDate,
        DateTime? toDate,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var localNow = TimeZoneInfo.ConvertTime(now, BusinessTimeZone);
        var today = DateOnly.FromDateTime(localNow.DateTime);

        var requestedFrom = fromDate.HasValue
            ? DateOnly.FromDateTime(fromDate.Value)
            : (DateOnly?)null;
        var requestedTo = toDate.HasValue
            ? DateOnly.FromDateTime(toDate.Value)
            : (DateOnly?)null;

        var range = ResolveRange(
            preset,
            requestedFrom,
            requestedTo,
            today);

        var startUtc = ToUtc(range.FromDate);
        var endUtcExclusive = ToUtc(range.ToDate.AddDays(1));
        var previousStartUtc = ToUtc(range.PreviousFromDate);
        var previousEndUtcExclusive = ToUtc(
            range.PreviousToDate.AddDays(1));

        var currentPayments = await _dbContext.PaymentTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.Status == PaymentStatus.Paid
                && transaction.CompletedAt != null
                && transaction.CompletedAt >= startUtc
                && transaction.CompletedAt < endUtcExclusive)
            .Select(transaction => new DashboardPaymentSnapshot
            {
                OrderId = transaction.OrderId,
                Amount = transaction.Amount,
                Method = transaction.Method,
                CompletedAt = transaction.CompletedAt!.Value
            })
            .ToListAsync(cancellationToken);

        var previousPayments = await _dbContext.PaymentTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.Status == PaymentStatus.Paid
                && transaction.CompletedAt != null
                && transaction.CompletedAt >= previousStartUtc
                && transaction.CompletedAt < previousEndUtcExclusive)
            .Select(transaction => new DashboardPaymentSnapshot
            {
                OrderId = transaction.OrderId,
                Amount = transaction.Amount,
                Method = transaction.Method,
                CompletedAt = transaction.CompletedAt!.Value
            })
            .ToListAsync(cancellationToken);

        var currentOrders = await _dbContext.Orders
            .AsNoTracking()
            .Where(order =>
                order.CreatedAt >= startUtc
                && order.CreatedAt < endUtcExclusive)
            .Select(order => new DashboardOrderSnapshot
            {
                OrderId = order.Id,
                CreatedAt = order.CreatedAt,
                OrderStatus = order.OrderStatus,
                PaymentStatus = order.PaymentStatus,
                FulfillmentStatus = order.FulfillmentStatus
            })
            .ToListAsync(cancellationToken);

        var previousOrders = await _dbContext.Orders
            .AsNoTracking()
            .Where(order =>
                order.CreatedAt >= previousStartUtc
                && order.CreatedAt < previousEndUtcExclusive)
            .Select(order => new DashboardOrderSnapshot
            {
                OrderId = order.Id,
                CreatedAt = order.CreatedAt,
                OrderStatus = order.OrderStatus,
                PaymentStatus = order.PaymentStatus,
                FulfillmentStatus = order.FulfillmentStatus
            })
            .ToListAsync(cancellationToken);

        var currentRefunds = await _dbContext.ReturnItems
            .AsNoTracking()
            .Where(item =>
                item.ReturnRequest.CompletedAt != null
                && (item.ReturnRequest.Status == ReturnRequestStatus.Refunded
                    || item.ReturnRequest.Status == ReturnRequestStatus.Closed)
                && item.ReturnRequest.CompletedAt >= startUtc
                && item.ReturnRequest.CompletedAt < endUtcExclusive)
            .Select(item => new DashboardRefundSnapshot
            {
                Amount = item.RefundAmount,
                CompletedAt = item.ReturnRequest.CompletedAt!.Value
            })
            .ToListAsync(cancellationToken);

        var previousRefunds = await _dbContext.ReturnItems
            .AsNoTracking()
            .Where(item =>
                item.ReturnRequest.CompletedAt != null
                && (item.ReturnRequest.Status == ReturnRequestStatus.Refunded
                    || item.ReturnRequest.Status == ReturnRequestStatus.Closed)
                && item.ReturnRequest.CompletedAt >= previousStartUtc
                && item.ReturnRequest.CompletedAt < previousEndUtcExclusive)
            .Select(item => new DashboardRefundSnapshot
            {
                Amount = item.RefundAmount,
                CompletedAt = item.ReturnRequest.CompletedAt!.Value
            })
            .ToListAsync(cancellationToken);

        var paidRevenue = currentPayments.Sum(item => item.Amount);
        var previousPaidRevenue = previousPayments.Sum(item => item.Amount);
        var refundAmount = currentRefunds.Sum(item => item.Amount);
        var previousRefundAmount = previousRefunds.Sum(item => item.Amount);
        var netRevenue = paidRevenue - refundAmount;
        var previousNetRevenue =
            previousPaidRevenue - previousRefundAmount;

        var paidOrderCount = currentPayments
            .Select(item => item.OrderId)
            .Distinct()
            .Count();
        var previousPaidOrderCount = previousPayments
            .Select(item => item.OrderId)
            .Distinct()
            .Count();

        var averageOrderValue = paidOrderCount == 0
            ? 0m
            : paidRevenue / paidOrderCount;
        var previousAverageOrderValue = previousPaidOrderCount == 0
            ? 0m
            : previousPaidRevenue / previousPaidOrderCount;

        var returnRequestsInPeriod = await _dbContext.ReturnRequests
            .AsNoTracking()
            .CountAsync(
                request =>
                    request.RequestedAt >= startUtc
                    && request.RequestedAt < endUtcExclusive,
                cancellationToken);

        var previousReturnRequests = await _dbContext.ReturnRequests
            .AsNoTracking()
            .CountAsync(
                request =>
                    request.RequestedAt >= previousStartUtc
                    && request.RequestedAt < previousEndUtcExclusive,
                cancellationToken);

        var trend = BuildTrend(
            range,
            currentPayments,
            currentRefunds,
            currentOrders);

        var topProducts = await _dbContext.OrderItems
            .AsNoTracking()
            .Where(item =>
                item.Order.CreatedAt >= startUtc
                && item.Order.CreatedAt < endUtcExclusive
                && (item.Order.PaymentStatus == PaymentStatus.Paid
                    || item.Order.PaymentStatus == PaymentStatus.PartiallyRefunded
                    || item.Order.PaymentStatus == PaymentStatus.Refunded))
            .GroupBy(item => new
            {
                item.ProductId,
                item.ProductName
            })
            .Select(group => new AdminDashboardTopProductViewModel
            {
                ProductId = group.Key.ProductId,
                ProductName = group.Key.ProductName,
                Quantity = group.Sum(item => item.Quantity),
                OrderCount = group
                    .Select(item => item.OrderId)
                    .Distinct()
                    .Count(),
                Revenue = group.Sum(item => item.LineTotal)
            })
            .OrderByDescending(item => item.Revenue)
            .ThenByDescending(item => item.Quantity)
            .Take(6)
            .ToListAsync(cancellationToken);

        var maximumTopRevenue = topProducts.Count == 0
            ? 0m
            : topProducts.Max(item => item.Revenue);

        topProducts = topProducts
            .Select(item => new AdminDashboardTopProductViewModel
            {
                ProductId = item.ProductId,
                ProductName = item.ProductName,
                Quantity = item.Quantity,
                OrderCount = item.OrderCount,
                Revenue = item.Revenue,
                PercentageOfTopRevenue = maximumTopRevenue <= 0
                    ? 0
                    : Math.Round(
                        item.Revenue * 100m / maximumTopRevenue,
                        2)
            })
            .ToList();

        var activeVariants = _dbContext.ProductVariants
            .AsNoTracking()
            .Where(variant =>
                variant.IsActive
                && variant.Product.IsActive);

        var nowUtc = now.UtcDateTime;

        var availableCampaigns = _dbContext.PriceCampaigns
            .AsNoTracking()
            .Where(campaign =>
                campaign.IsActive
                && campaign.EndDate != null
                && campaign.EndDate > nowUtc);

        var awaitingConfirmationCount = await _dbContext.Orders
            .AsNoTracking()
            .CountAsync(
                order => order.OrderStatus == OrderStatus.Placed,
                cancellationToken);

        var readyToShipCount = await _dbContext.Orders
            .AsNoTracking()
            .CountAsync(
                order =>
                    order.FulfillmentStatus
                        == FulfillmentStatus.ReadyToShip,
                cancellationToken);

        var shippingExceptionCount = await _dbContext.Shipments
            .AsNoTracking()
            .CountAsync(
                shipment =>
                    shipment.Direction == ShipmentDirection.Outbound
                    && (shipment.Status == ShipmentStatus.DeliveryFailed
                        || shipment.Status == ShipmentStatus.Exception),
                cancellationToken);

        var openReturnCount = await _dbContext.ReturnRequests
            .AsNoTracking()
            .CountAsync(
                request => OpenReturnStatuses.Contains(request.Status),
                cancellationToken);

        var activeProductCount = await _dbContext.Products
            .AsNoTracking()
            .CountAsync(
                product => product.IsActive,
                cancellationToken);

        var lowStockVariantCount = await activeVariants
            .CountAsync(
                variant =>
                    variant.StockQuantity > 0
                    && variant.StockQuantity <= LowStockThreshold,
                cancellationToken);

        var outOfStockVariantCount = await activeVariants
            .CountAsync(
                variant => variant.StockQuantity == 0,
                cancellationToken);

        var runningCampaignCount = await availableCampaigns
            .CountAsync(
                campaign => campaign.StartDate <= nowUtc,
                cancellationToken);

        var recentOrders = await _dbContext.Orders
            .AsNoTracking()
            .Where(order =>
                order.CreatedAt >= startUtc
                && order.CreatedAt < endUtcExclusive)
            .OrderByDescending(order => order.CreatedAt)
            .Take(DashboardItemLimit)
            .Select(order => new AdminDashboardRecentOrderViewModel
            {
                OrderId = order.Id,
                Code = order.Code,
                CustomerName = order.CustomerName,
                GrandTotal = order.GrandTotal,
                OrderStatus = order.OrderStatus,
                PaymentStatus = order.PaymentStatus,
                FulfillmentStatus = order.FulfillmentStatus,
                CreatedAt = order.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var lowStockItems = await activeVariants
            .Where(variant =>
                variant.StockQuantity <= LowStockThreshold)
            .OrderBy(variant => variant.StockQuantity)
            .ThenBy(variant => variant.Product.Name)
            .ThenBy(variant => variant.SKU)
            .Take(DashboardItemLimit)
            .Select(variant =>
                new AdminDashboardLowStockItemViewModel
                {
                    ProductId = variant.ProductId,
                    ProductName = variant.Product.Name,
                    VariantId = variant.Id,
                    SKU = variant.SKU,
                    StockQuantity = variant.StockQuantity,
                    CurrentPrice = variant.CurrentPrice
                })
            .ToListAsync(cancellationToken);

        var campaigns = await availableCampaigns
            .OrderBy(campaign =>
                campaign.StartDate <= nowUtc ? 0 : 1)
            .ThenBy(campaign => campaign.StartDate)
            .Take(DashboardItemLimit)
            .Select(campaign =>
                new AdminDashboardCampaignItemViewModel
                {
                    CampaignId = campaign.Id,
                    Name = campaign.Name,
                    StartDate = campaign.StartDate,
                    EndDate = campaign.EndDate!.Value,
                    IsRunning = campaign.StartDate <= nowUtc,
                    VariantCount = campaign.CampaignItems.Count
                })
            .ToListAsync(cancellationToken);

        var model = new AdminDashboardViewModel
        {
            GeneratedAtUtc = nowUtc,
            Filter = new AdminDashboardFilterViewModel
            {
                Preset = range.Preset,
                FromDate = range.FromDate,
                ToDate = range.ToDate,
                PreviousFromDate = range.PreviousFromDate,
                PreviousToDate = range.PreviousToDate,
                RangeLabel = FormatRangeLabel(
                    range.FromDate,
                    range.ToDate),
                PreviousRangeLabel = FormatRangeLabel(
                    range.PreviousFromDate,
                    range.PreviousToDate),
                DayCount = range.DayCount,
                Notice = range.Notice
            },
            PaidRevenue = paidRevenue,
            RefundAmount = refundAmount,
            NetRevenue = netRevenue,
            AverageOrderValue = averageOrderValue,
            OrdersInPeriod = currentOrders.Count,
            PaidOrdersInPeriod = paidOrderCount,
            ReturnRequestsInPeriod = returnRequestsInPeriod,
            CancelledOrdersInPeriod = currentOrders.Count(
                order => order.OrderStatus == OrderStatus.Cancelled),
            OrdersTodayCount = currentOrders.Count,
            PaidRevenueToday = paidRevenue,
            Kpis =
            [
                BuildKpi(
                    "paid-revenue",
                    "Doanh thu đã thu",
                    "Giao dịch đã hoàn tất trong kỳ",
                    "fa-coins",
                    "success",
                    paidRevenue,
                    previousPaidRevenue,
                    AdminDashboardMetricFormat.Currency),
                BuildKpi(
                    "net-revenue",
                    "Doanh thu sau hoàn",
                    "Doanh thu đã thu trừ khoản hoàn tất",
                    "fa-receipt",
                    "info",
                    netRevenue,
                    previousNetRevenue,
                    AdminDashboardMetricFormat.Currency),
                BuildKpi(
                    "orders",
                    "Đơn phát sinh",
                    "Đơn được tạo trong khoảng đã chọn",
                    "fa-bag-shopping",
                    "info",
                    currentOrders.Count,
                    previousOrders.Count,
                    AdminDashboardMetricFormat.Number),
                BuildKpi(
                    "paid-orders",
                    "Đơn đã thanh toán",
                    "Số đơn có giao dịch hoàn tất",
                    "fa-circle-check",
                    "success",
                    paidOrderCount,
                    previousPaidOrderCount,
                    AdminDashboardMetricFormat.Number),
                BuildKpi(
                    "average-order",
                    "Giá trị đơn trung bình",
                    "Doanh thu đã thu trên mỗi đơn",
                    "fa-receipt",
                    "info",
                    averageOrderValue,
                    previousAverageOrderValue,
                    AdminDashboardMetricFormat.Currency),
                BuildKpi(
                    "refunds",
                    "Hoàn tiền đã xử lý",
                    $"{returnRequestsInPeriod} yêu cầu phát sinh trong kỳ",
                    "fa-rotate-left",
                    "warning",
                    refundAmount,
                    previousRefundAmount,
                    AdminDashboardMetricFormat.Currency,
                    lowerIsBetter: true)
            ],
            Trend = trend,
            OrderStatuses = BuildOrderStatusBreakdown(currentOrders),
            PaymentMethods = BuildPaymentMethodBreakdown(
                currentPayments),
            TopProducts = topProducts,
            AwaitingConfirmationCount = awaitingConfirmationCount,
            ReadyToShipCount = readyToShipCount,
            ShippingExceptionCount = shippingExceptionCount,
            OpenReturnCount = openReturnCount,
            ActiveProductCount = activeProductCount,
            LowStockVariantCount = lowStockVariantCount,
            OutOfStockVariantCount = outOfStockVariantCount,
            RunningCampaignCount = runningCampaignCount,
            RecentOrders = recentOrders,
            LowStockItems = lowStockItems,
            Campaigns = campaigns
        };

        return View(model);
    }

    private static AdminDashboardKpiViewModel BuildKpi(
        string key,
        string label,
        string description,
        string iconClass,
        string tone,
        decimal value,
        decimal previousValue,
        AdminDashboardMetricFormat format,
        bool lowerIsBetter = false)
    {
        return new AdminDashboardKpiViewModel
        {
            Key = key,
            Label = label,
            Description = description,
            IconClass = iconClass,
            Tone = tone,
            Value = value,
            PreviousValue = previousValue,
            ChangePercent = CalculateChangePercent(
                value,
                previousValue),
            LowerIsBetter = lowerIsBetter,
            Format = format
        };
    }

    private static decimal? CalculateChangePercent(
        decimal current,
        decimal previous)
    {
        if (previous == 0m)
        {
            return current == 0m
                ? 0m
                : null;
        }

        return Math.Round(
            (current - previous)
            / Math.Abs(previous)
            * 100m,
            1);
    }

    private static IReadOnlyList<AdminDashboardTrendPointViewModel>
        BuildTrend(
            DashboardRange range,
            IReadOnlyList<DashboardPaymentSnapshot> payments,
            IReadOnlyList<DashboardRefundSnapshot> refunds,
            IReadOnlyList<DashboardOrderSnapshot> orders)
    {
        var revenueByDate = payments
            .GroupBy(item =>
                ToBusinessDate(item.CompletedAt))
            .ToDictionary(
                group => group.Key,
                group => group.Sum(item => item.Amount));

        var refundsByDate = refunds
            .GroupBy(item =>
                ToBusinessDate(item.CompletedAt))
            .ToDictionary(
                group => group.Key,
                group => group.Sum(item => item.Amount));

        var ordersByDate = orders
            .GroupBy(item =>
                ToBusinessDate(item.CreatedAt))
            .ToDictionary(
                group => group.Key,
                group => group.Count());

        var labelStep = range.DayCount switch
        {
            <= 10 => 1,
            <= 31 => 3,
            <= 90 => 7,
            <= 180 => 14,
            _ => 30
        };

        return Enumerable.Range(0, range.DayCount)
            .Select(index =>
            {
                var date = range.FromDate.AddDays(index);
                var revenue = revenueByDate.GetValueOrDefault(date);
                var refund = refundsByDate.GetValueOrDefault(date);

                return new AdminDashboardTrendPointViewModel
                {
                    Date = date,
                    Label = date.ToString("dd/MM"),
                    PaidRevenue = revenue,
                    RefundAmount = refund,
                    NetRevenue = revenue - refund,
                    OrderCount = ordersByDate.GetValueOrDefault(date),
                    ShowAxisLabel =
                        index == 0
                        || index == range.DayCount - 1
                        || index % labelStep == 0
                };
            })
            .ToArray();
    }

    private static IReadOnlyList<AdminDashboardBreakdownItemViewModel>
        BuildOrderStatusBreakdown(
            IReadOnlyList<DashboardOrderSnapshot> orders)
    {
        var total = orders.Count;

        var rows = new[]
        {
            new
            {
                Key = "pending-payment",
                Label = "Chờ thanh toán",
                Count = orders.Count(item =>
                    item.OrderStatus == OrderStatus.PendingPayment),
                Tone = "warning"
            },
            new
            {
                Key = "placed",
                Label = "Chờ xác nhận",
                Count = orders.Count(item =>
                    item.OrderStatus == OrderStatus.Placed),
                Tone = "info"
            },
            new
            {
                Key = "processing",
                Label = "Đang xử lý",
                Count = orders.Count(item =>
                    item.OrderStatus == OrderStatus.Confirmed
                    || item.OrderStatus == OrderStatus.Processing),
                Tone = "info"
            },
            new
            {
                Key = "completed",
                Label = "Hoàn tất",
                Count = orders.Count(item =>
                    item.OrderStatus == OrderStatus.Completed),
                Tone = "success"
            },
            new
            {
                Key = "cancelled",
                Label = "Đã hủy",
                Count = orders.Count(item =>
                    item.OrderStatus == OrderStatus.Cancelled),
                Tone = "danger"
            },
            new
            {
                Key = "closed",
                Label = "Đã đóng",
                Count = orders.Count(item =>
                    item.OrderStatus == OrderStatus.Closed),
                Tone = "neutral"
            }
        };

        return rows
            .Select(row =>
                new AdminDashboardBreakdownItemViewModel
                {
                    Key = row.Key,
                    Label = row.Label,
                    Count = row.Count,
                    Percentage = total == 0
                        ? 0
                        : Math.Round(
                            row.Count * 100m / total,
                            1),
                    Tone = row.Tone
                })
            .ToArray();
    }

    private static IReadOnlyList<AdminDashboardBreakdownItemViewModel>
        BuildPaymentMethodBreakdown(
            IReadOnlyList<DashboardPaymentSnapshot> payments)
    {
        var total = payments.Sum(item => item.Amount);

        return payments
            .GroupBy(item =>
                string.IsNullOrWhiteSpace(item.Method)
                    ? "Khác"
                    : item.Method.Trim())
            .Select(group =>
                new AdminDashboardBreakdownItemViewModel
                {
                    Key = group.Key,
                    Label = PaymentMethodLabel(group.Key),
                    Count = group
                        .Select(item => item.OrderId)
                        .Distinct()
                        .Count(),
                    Amount = group.Sum(item => item.Amount),
                    Percentage = total <= 0
                        ? 0
                        : Math.Round(
                            group.Sum(item => item.Amount)
                            * 100m
                            / total,
                            1),
                    Tone = PaymentMethodTone(group.Key)
                })
            .OrderByDescending(item => item.Amount)
            .ToArray();
    }

    private static string PaymentMethodLabel(string method)
    {
        return method.Trim().ToUpperInvariant() switch
        {
            "COD" => "Thanh toán khi nhận hàng",
            "VNPAY" => "VNPay",
            "BANKTRANSFER" => "Chuyển khoản",
            "BANK_TRANSFER" => "Chuyển khoản",
            _ => method
        };
    }

    private static string PaymentMethodTone(string method)
    {
        return method.Trim().ToUpperInvariant() switch
        {
            "COD" => "warning",
            "VNPAY" => "success",
            _ => "info"
        };
    }

    private static DashboardRange ResolveRange(
        string? preset,
        DateOnly? requestedFrom,
        DateOnly? requestedTo,
        DateOnly today)
    {
        var normalizedPreset = string.IsNullOrWhiteSpace(preset)
            ? requestedFrom.HasValue || requestedTo.HasValue
                ? "custom"
                : "last7"
            : preset.Trim().ToLowerInvariant();

        DateOnly from;
        DateOnly to;

        switch (normalizedPreset)
        {
            case "today":
                from = today;
                to = today;
                break;
            case "yesterday":
                from = today.AddDays(-1);
                to = from;
                break;
            case "last30":
                from = today.AddDays(-29);
                to = today;
                break;
            case "thismonth":
                from = new DateOnly(
                    today.Year,
                    today.Month,
                    1);
                to = today;
                break;
            case "custom":
                from = requestedFrom
                    ?? today.AddDays(-6);
                to = requestedTo
                    ?? today;
                break;
            default:
                normalizedPreset = "last7";
                from = today.AddDays(-6);
                to = today;
                break;
        }

        string? notice = null;

        if (from > to)
        {
            (from, to) = (to, from);
            notice =
                "Đã tự động đổi thứ tự ngày bắt đầu và ngày kết thúc.";
        }

        if (to > today)
        {
            to = today;
            if (from > to)
            {
                from = to;
            }

            notice =
                "Ngày kết thúc đã được giới hạn đến ngày hiện tại.";
        }

        var dayCount = to.DayNumber - from.DayNumber + 1;
        if (dayCount > MaximumDashboardRangeDays)
        {
            from = to.AddDays(
                -(MaximumDashboardRangeDays - 1));
            dayCount = MaximumDashboardRangeDays;
            notice =
                $"Dashboard hiển thị tối đa {MaximumDashboardRangeDays} ngày gần nhất.";
        }

        var previousTo = from.AddDays(-1);
        var previousFrom = previousTo.AddDays(
            -(dayCount - 1));

        return new DashboardRange(
            normalizedPreset,
            from,
            to,
            previousFrom,
            previousTo,
            dayCount,
            notice);
    }

    private static DateTime ToUtc(DateOnly date)
    {
        var unspecified = DateTime.SpecifyKind(
            date.ToDateTime(TimeOnly.MinValue),
            DateTimeKind.Unspecified);

        return TimeZoneInfo.ConvertTimeToUtc(
            unspecified,
            BusinessTimeZone);
    }

    private static DateOnly ToBusinessDate(DateTime utcValue)
    {
        var normalizedUtc = DateTime.SpecifyKind(
            utcValue,
            DateTimeKind.Utc);

        return DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(
                normalizedUtc,
                BusinessTimeZone));
    }

    private static TimeZoneInfo ResolveBusinessTimeZone()
    {
        var identifiers = new[]
        {
            "Asia/Ho_Chi_Minh",
            "SE Asia Standard Time"
        };

        foreach (var identifier in identifiers)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(
                    identifier);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Local;
    }

    private static string FormatRangeLabel(
        DateOnly from,
        DateOnly to)
    {
        return from == to
            ? from.ToString("dd/MM/yyyy")
            : $"{from:dd/MM/yyyy} – {to:dd/MM/yyyy}";
    }

    private sealed record DashboardRange(
        string Preset,
        DateOnly FromDate,
        DateOnly ToDate,
        DateOnly PreviousFromDate,
        DateOnly PreviousToDate,
        int DayCount,
        string? Notice);

    private sealed class DashboardPaymentSnapshot
    {
        public int OrderId { get; init; }
        public decimal Amount { get; init; }
        public string Method { get; init; } = string.Empty;
        public DateTime CompletedAt { get; init; }
    }

    private sealed class DashboardRefundSnapshot
    {
        public decimal Amount { get; init; }
        public DateTime CompletedAt { get; init; }
    }

    private sealed class DashboardOrderSnapshot
    {
        public int OrderId { get; init; }
        public DateTime CreatedAt { get; init; }
        public OrderStatus OrderStatus { get; init; }
        public PaymentStatus PaymentStatus { get; init; }
        public FulfillmentStatus FulfillmentStatus { get; init; }
    }
}
