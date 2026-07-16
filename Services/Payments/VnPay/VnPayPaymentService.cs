using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Commerce.Inventory;
using WebApplication2.Services.Commerce.Orders;

namespace WebApplication2.Services.Payments.VnPay;

public sealed class VnPayPaymentService : IVnPayPaymentService
{
    private const string ProviderName = "VNPAY";
    private const string ActorName = "VNPay";

    private readonly ApplicationDbContext _context;
    private readonly IVnPayGateway _gateway;
    private readonly IInventoryService _inventoryService;
    private readonly VnPayOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<VnPayPaymentService> _logger;

    public VnPayPaymentService(
        ApplicationDbContext context,
        IVnPayGateway gateway,
        IInventoryService inventoryService,
        IOptions<VnPayOptions> options,
        TimeProvider timeProvider,
        ILogger<VnPayPaymentService> logger)
    {
        _context = context;
        _gateway = gateway;
        _inventoryService = inventoryService;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<VnPayPaymentLinkResult> CreatePaymentUrlAsync(
        int orderId,
        int customerId,
        string clientIpAddress,
        CancellationToken cancellationToken)
    {
        if (orderId <= 0 || customerId <= 0)
        {
            return VnPayPaymentLinkResult.Failure(
                "INVALID_PAYMENT_ORDER",
                "Đơn hàng thanh toán không hợp lệ.");
        }

        var order = await _context.Orders
            .Include(item => item.PaymentTransactions)
            .SingleOrDefaultAsync(
                item =>
                    item.Id == orderId
                    && item.CustomerId == customerId,
                cancellationToken);

        if (order is null)
        {
            return VnPayPaymentLinkResult.Failure(
                "PAYMENT_ORDER_NOT_FOUND",
                "Không tìm thấy đơn hàng cần thanh toán.");
        }

        if (order.OrderStatus != OrderStatus.PendingPayment
            || order.PaymentStatus != PaymentStatus.Pending)
        {
            return VnPayPaymentLinkResult.Failure(
                "PAYMENT_ORDER_NOT_PENDING",
                "Đơn hàng không còn ở trạng thái chờ thanh toán.",
                order.PublicToken);
        }

        var transaction = order.PaymentTransactions
            .Where(item =>
                item.Provider == ProviderName
                && item.Status == PaymentStatus.Pending)
            .OrderByDescending(item => item.AttemptNumber)
            .ThenByDescending(item => item.Id)
            .FirstOrDefault();

        if (transaction is null)
        {
            return VnPayPaymentLinkResult.Failure(
                "PAYMENT_TRANSACTION_NOT_FOUND",
                "Không tìm thấy giao dịch VNPay đang chờ xử lý.",
                order.PublicToken);
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var paymentExpiresAtUtc = transaction.CreatedAt.AddMinutes(
            Math.Clamp(_options.PaymentTimeoutMinutes, 5, 60));

        if (nowUtc >= paymentExpiresAtUtc)
        {
            return VnPayPaymentLinkResult.Failure(
                "VNPAY_PAYMENT_EXPIRED",
                "Thời gian thanh toán của đơn hàng đã hết.",
                order.PublicToken);
        }

        var gatewayResult = _gateway.CreatePaymentUrl(
            new VnPayPaymentRequest(
                transaction.MerchantReference,
                transaction.Amount,
                $"Thanh toan don hang {order.Code}",
                clientIpAddress,
                transaction.CreatedAt));

        if (!gatewayResult.Success
            || string.IsNullOrWhiteSpace(gatewayResult.PaymentUrl))
        {
            return VnPayPaymentLinkResult.Failure(
                gatewayResult.ErrorCode ?? "VNPAY_URL_FAILED",
                gatewayResult.Message,
                order.PublicToken);
        }

        transaction.RequestPayload = gatewayResult.RequestPayload;
        transaction.UpdatedAt = nowUtc;

        await _context.SaveChangesAsync(cancellationToken);

        return new VnPayPaymentLinkResult(
            true,
            gatewayResult.PaymentUrl,
            order.PublicToken,
            null,
            "Đang chuyển tới cổng thanh toán VNPay.");
    }

    public async Task<VnPayCallbackProcessResult> ProcessCallbackAsync(
        IReadOnlyDictionary<string, string> parameters,
        string source,
        CancellationToken cancellationToken)
    {
        var callback = _gateway.ParseCallback(parameters);

        if (!callback.SignatureValid)
        {
            return Result(
                "97",
                callback.Message,
                callback,
                alreadyProcessed: false);
        }

        if (string.IsNullOrWhiteSpace(callback.MerchantReference))
        {
            return Result(
                "01",
                "Không tìm thấy mã tham chiếu giao dịch.",
                callback,
                alreadyProcessed: false);
        }

        IDbContextTransaction? transactionScope = null;

        try
        {
            transactionScope = await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            var payment = await _context.PaymentTransactions
                .Include(item => item.Order)
                    .ThenInclude(order => order.Items)
                .Include(item => item.Order)
                    .ThenInclude(order => order.Shipments)
                .SingleOrDefaultAsync(
                    item =>
                        item.Provider == ProviderName
                        && item.MerchantReference == callback.MerchantReference,
                    cancellationToken);

            if (payment is null)
            {
                await transactionScope.RollbackAsync(cancellationToken);

                return Result(
                    "01",
                    "Không tìm thấy giao dịch tại FastBuy.",
                    callback,
                    alreadyProcessed: false);
            }

            var order = payment.Order;
            var purchasedVariantIds = order.Items
                .OrderBy(item => item.Id)
                .Select(item => item.ProductVariantId)
                .Distinct()
                .ToArray();

            if (payment.Amount != callback.Amount
                || order.GrandTotal != callback.Amount)
            {
                await transactionScope.RollbackAsync(cancellationToken);

                return new VnPayCallbackProcessResult(
                    "04",
                    "Số tiền giao dịch không khớp đơn hàng.",
                    true,
                    false,
                    false,
                    order.PublicToken,
                    order.CustomerId,
                    ExtractCheckoutClientRequestId(order.ClientRequestId),
                    purchasedVariantIds);
            }

            if (payment.Status != PaymentStatus.Pending
                || order.OrderStatus != OrderStatus.PendingPayment
                || order.PaymentStatus != PaymentStatus.Pending)
            {
                await transactionScope.CommitAsync(cancellationToken);

                return new VnPayCallbackProcessResult(
                    "02",
                    "Giao dịch đã được xác nhận trước đó.",
                    true,
                    payment.Status == PaymentStatus.Paid,
                    true,
                    order.PublicToken,
                    order.CustomerId,
                    ExtractCheckoutClientRequestId(order.ClientRequestId),
                    purchasedVariantIds);
            }

            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var actor = NormalizeSource(source);
            payment.ResponsePayload = callback.RawPayload;
            payment.ProviderTransactionId = string.Equals(
                callback.ProviderTransactionId,
                "0",
                StringComparison.Ordinal)
                ? null
                : callback.ProviderTransactionId;
            payment.CompletedAt = callback.PaidAtUtc ?? nowUtc;
            payment.UpdatedAt = nowUtc;

            if (callback.PaymentSucceeded)
            {
                payment.Status = PaymentStatus.Paid;
                payment.FailureCode = null;
                payment.FailureMessage = null;

                order.PaymentStatus = PaymentStatus.Paid;
                order.OrderStatus = OrderStatus.Placed;
                order.FulfillmentStatus = FulfillmentStatus.Unfulfilled;
                order.PlacedAt ??= nowUtc;
                order.CancelledAt = null;
                order.CancelReason = null;
                order.UpdatedAt = nowUtc;

                _context.OrderStatusHistories.AddRange(
                    new OrderStatusHistory
                    {
                        OrderId = order.Id,
                        Category = OrderHistoryCategory.Payment,
                        FromStatus = PaymentStatus.Pending.ToString(),
                        ToStatus = PaymentStatus.Paid.ToString(),
                        Code = "VNPAY_PAYMENT_PAID",
                        Title = "Thanh toán VNPay thành công",
                        Description = "VNPay đã xác nhận giao dịch và số tiền thanh toán.",
                        ChangedBy = actor,
                        CustomerVisible = true,
                        OccurredAt = nowUtc,
                        CorrelationId = callback.MerchantReference
                    },
                    new OrderStatusHistory
                    {
                        OrderId = order.Id,
                        Category = OrderHistoryCategory.Order,
                        FromStatus = OrderStatus.PendingPayment.ToString(),
                        ToStatus = OrderStatus.Placed.ToString(),
                        Code = "ORDER_PLACED_AFTER_VNPAY",
                        Title = "Đơn hàng đã được ghi nhận",
                        Description = "Đơn hàng chuyển sang xử lý sau khi thanh toán thành công.",
                        ChangedBy = actor,
                        CustomerVisible = true,
                        OccurredAt = nowUtc,
                        CorrelationId = callback.MerchantReference
                    });

                await _inventoryService.CommitAsync(
                    order.Id,
                    callback.MerchantReference,
                    ActorName,
                    cancellationToken);
            }
            else
            {
                payment.Status = PaymentStatus.Failed;
                payment.FailureCode = string.IsNullOrWhiteSpace(
                    callback.ResponseCode)
                    ? "VNPAY_PAYMENT_FAILED"
                    : callback.ResponseCode;
                payment.FailureMessage =
                    "VNPay thông báo giao dịch chưa thành công.";

                order.PaymentStatus = PaymentStatus.Failed;
                order.OrderStatus = OrderStatus.Cancelled;
                order.FulfillmentStatus = FulfillmentStatus.Cancelled;
                order.CancelledAt = nowUtc;
                order.CancelReason =
                    $"Thanh toán VNPay chưa thành công (mã {payment.FailureCode}).";
                order.UpdatedAt = nowUtc;

                foreach (var shipment in order.Shipments.Where(item =>
                             item.Direction == ShipmentDirection.Outbound
                             && item.ExternalOrderCode == null
                             && item.Status is (
                                 ShipmentStatus.Draft
                                 or ShipmentStatus.PendingCreation)))
                {
                    shipment.Status = ShipmentStatus.Cancelled;
                    shipment.CancelledAt = nowUtc;
                    shipment.ProviderReason =
                        "Đơn hàng bị hủy do thanh toán VNPay chưa thành công.";
                    shipment.UpdatedAt = nowUtc;
                }

                _context.OrderStatusHistories.AddRange(
                    new OrderStatusHistory
                    {
                        OrderId = order.Id,
                        Category = OrderHistoryCategory.Payment,
                        FromStatus = PaymentStatus.Pending.ToString(),
                        ToStatus = PaymentStatus.Failed.ToString(),
                        Code = "VNPAY_PAYMENT_FAILED",
                        Title = "Thanh toán VNPay chưa thành công",
                        Description = order.CancelReason,
                        ChangedBy = actor,
                        CustomerVisible = true,
                        OccurredAt = nowUtc,
                        CorrelationId = callback.MerchantReference
                    },
                    new OrderStatusHistory
                    {
                        OrderId = order.Id,
                        Category = OrderHistoryCategory.Order,
                        FromStatus = OrderStatus.PendingPayment.ToString(),
                        ToStatus = OrderStatus.Cancelled.ToString(),
                        Code = "ORDER_CANCELLED_AFTER_VNPAY",
                        Title = "Đơn hàng đã được hủy",
                        Description =
                            "Tồn kho đã được hoàn lại vì giao dịch chưa thành công.",
                        ChangedBy = actor,
                        CustomerVisible = true,
                        OccurredAt = nowUtc,
                        CorrelationId = callback.MerchantReference
                    });

                await _inventoryService.ReleaseAsync(
                    order.Id,
                    order.CancelReason,
                    $"{callback.MerchantReference}:payment-release",
                    ActorName,
                    cancellationToken);
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transactionScope.CommitAsync(cancellationToken);

            return new VnPayCallbackProcessResult(
                "00",
                "Đã ghi nhận kết quả giao dịch.",
                true,
                callback.PaymentSucceeded,
                false,
                order.PublicToken,
                order.CustomerId,
                ExtractCheckoutClientRequestId(order.ClientRequestId),
                purchasedVariantIds);
        }
        catch (Exception exception)
        {
            if (transactionScope is not null)
            {
                await transactionScope.RollbackAsync(cancellationToken);
            }

            _logger.LogError(
                exception,
                "VNPay callback processing failed for reference {MerchantReference}.",
                callback.MerchantReference);

            return Result(
                "99",
                "FastBuy chưa thể xử lý kết quả giao dịch.",
                callback,
                alreadyProcessed: false);
        }
        finally
        {
            if (transactionScope is not null)
            {
                await transactionScope.DisposeAsync();
            }
        }
    }


    public async Task<int> ExpirePendingPaymentsAsync(
        CancellationToken cancellationToken)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var expirationWindowMinutes =
            Math.Clamp(_options.PaymentTimeoutMinutes, 5, 60)
            + Math.Clamp(_options.ExpirationGraceMinutes, 1, 15);

        var cutoffUtc = nowUtc.AddMinutes(-expirationWindowMinutes);

        var paymentIds = await _context.PaymentTransactions
            .AsNoTracking()
            .Where(payment =>
                payment.Provider == ProviderName
                && payment.Status == PaymentStatus.Pending
                && payment.CreatedAt <= cutoffUtc
                && payment.Order.OrderStatus == OrderStatus.PendingPayment
                && payment.Order.PaymentStatus == PaymentStatus.Pending)
            .OrderBy(payment => payment.CreatedAt)
            .ThenBy(payment => payment.Id)
            .Select(payment => payment.Id)
            .Take(Math.Clamp(_options.ExpirationBatchSize, 1, 100))
            .ToArrayAsync(cancellationToken);

        var expiredCount = 0;

        foreach (var paymentId in paymentIds)
        {
            IDbContextTransaction? transactionScope = null;

            try
            {
                transactionScope =
                    await _context.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        cancellationToken);

                var payment = await _context.PaymentTransactions
                    .Include(item => item.Order)
                        .ThenInclude(order => order.Shipments)
                    .SingleOrDefaultAsync(
                        item => item.Id == paymentId,
                        cancellationToken);

                if (payment is null
                    || payment.Provider != ProviderName
                    || payment.Status != PaymentStatus.Pending
                    || payment.CreatedAt > cutoffUtc
                    || payment.Order.OrderStatus
                        != OrderStatus.PendingPayment
                    || payment.Order.PaymentStatus
                        != PaymentStatus.Pending)
                {
                    await transactionScope.CommitAsync(
                        cancellationToken);
                    continue;
                }

                var order = payment.Order;
                var expiredAtUtc =
                    _timeProvider.GetUtcNow().UtcDateTime;

                payment.Status = PaymentStatus.Failed;
                payment.FailureCode = "VNPAY_PAYMENT_EXPIRED";
                payment.FailureMessage =
                    "Thời gian thanh toán VNPay đã hết.";
                payment.CompletedAt = expiredAtUtc;
                payment.UpdatedAt = expiredAtUtc;

                order.OrderStatus = OrderStatus.Cancelled;
                order.PaymentStatus = PaymentStatus.Failed;
                order.FulfillmentStatus =
                    FulfillmentStatus.Cancelled;
                order.CancelledAt = expiredAtUtc;
                order.CancelReason =
                    "Đơn hàng đã hết thời gian thanh toán VNPay.";
                order.UpdatedAt = expiredAtUtc;

                foreach (var shipment in order.Shipments.Where(item =>
                             item.Direction
                                 == ShipmentDirection.Outbound
                             && item.ExternalOrderCode == null
                             && item.Status is (
                                 ShipmentStatus.Draft
                                 or ShipmentStatus.PendingCreation)))
                {
                    shipment.Status = ShipmentStatus.Cancelled;
                    shipment.CancelledAt = expiredAtUtc;
                    shipment.ProviderReason =
                        "Đơn hàng hết thời gian thanh toán VNPay.";
                    shipment.UpdatedAt = expiredAtUtc;
                }

                _context.OrderStatusHistories.AddRange(
                    new OrderStatusHistory
                    {
                        OrderId = order.Id,
                        Category =
                            OrderHistoryCategory.Payment,
                        FromStatus =
                            PaymentStatus.Pending.ToString(),
                        ToStatus =
                            PaymentStatus.Failed.ToString(),
                        Code = "VNPAY_PAYMENT_EXPIRED",
                        Title = "Thanh toán VNPay đã hết hạn",
                        Description =
                            "FastBuy chưa nhận được xác nhận thanh toán trong thời gian cho phép.",
                        ChangedBy = "VNPay Expiration Worker",
                        CustomerVisible = true,
                        OccurredAt = expiredAtUtc,
                        CorrelationId =
                            payment.MerchantReference
                    },
                    new OrderStatusHistory
                    {
                        OrderId = order.Id,
                        Category =
                            OrderHistoryCategory.Order,
                        FromStatus =
                            OrderStatus.PendingPayment.ToString(),
                        ToStatus =
                            OrderStatus.Cancelled.ToString(),
                        Code =
                            "ORDER_CANCELLED_PAYMENT_EXPIRED",
                        Title = "Đơn hàng đã được hủy",
                        Description =
                            "Tồn kho đã được hoàn lại sau khi thời gian thanh toán kết thúc.",
                        ChangedBy = "VNPay Expiration Worker",
                        CustomerVisible = true,
                        OccurredAt = expiredAtUtc,
                        CorrelationId =
                            payment.MerchantReference
                    });

                await _inventoryService.ReleaseAsync(
                    order.Id,
                    order.CancelReason,
                    $"{payment.MerchantReference}:expiration-release",
                    "VNPay Expiration Worker",
                    cancellationToken);

                await _context.SaveChangesAsync(
                    cancellationToken);
                await transactionScope.CommitAsync(
                    cancellationToken);

                expiredCount++;
            }
            catch (Exception exception)
            {
                if (transactionScope is not null)
                {
                    await transactionScope.RollbackAsync(
                        cancellationToken);
                }

                _logger.LogError(
                    exception,
                    "Could not expire pending VNPay payment {PaymentId}.",
                    paymentId);
            }
            finally
            {
                if (transactionScope is not null)
                {
                    await transactionScope.DisposeAsync();
                }

                _context.ChangeTracker.Clear();
            }
        }

        return expiredCount;
    }

    private static VnPayCallbackProcessResult Result(
        string rspCode,
        string message,
        VnPayCallbackData callback,
        bool alreadyProcessed)
    {
        return new VnPayCallbackProcessResult(
            rspCode,
            message,
            callback.SignatureValid,
            false,
            alreadyProcessed,
            null,
            null,
            null,
            []);
    }

    private static string NormalizeSource(string? source)
    {
        var normalized = string.IsNullOrWhiteSpace(source)
            ? ActorName
            : source.Trim();

        return normalized.Length <= 100
            ? normalized
            : normalized[..100];
    }

    private static string? ExtractCheckoutClientRequestId(
        string scopedClientRequestId)
    {
        var separator = scopedClientRequestId.LastIndexOf(':');
        var value = separator >= 0
            ? scopedClientRequestId[(separator + 1)..]
            : scopedClientRequestId;

        return Guid.TryParseExact(value, "N", out _)
            ? value
            : null;
    }
}
