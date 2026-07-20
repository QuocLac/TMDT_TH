using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Commerce.Inventory;

namespace WebApplication2.Services.Payments.Simulation;

public sealed class DevelopmentPaymentSimulator
    : IDevelopmentPaymentSimulator
{
    private const string VnPayProvider = "VNPAY";
    private const string SimulatorActor = "DevelopmentSimulator";

    private readonly ApplicationDbContext _context;
    private readonly IInventoryService _inventory;
    private readonly IWebHostEnvironment _environment;
    private readonly TimeProvider _timeProvider;

    public DevelopmentPaymentSimulator(
        ApplicationDbContext context,
        IInventoryService inventory,
        IWebHostEnvironment environment,
        TimeProvider timeProvider)
    {
        _context = context;
        _inventory = inventory;
        _environment = environment;
        _timeProvider = timeProvider;
    }

    public bool IsEnabled => _environment.IsDevelopment();

    public async Task<DevelopmentSimulationResult>
        ConfirmVnPayPaymentAsync(
            int orderId,
            string actor,
            CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return Disabled();
        }

        if (orderId <= 0)
        {
            return DevelopmentSimulationResult.Failure(
                "DEV_PAYMENT_ORDER_INVALID",
                "Mã đơn hàng không hợp lệ.");
        }

        await using var databaseTransaction =
            await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            var order = await _context.Orders
                .AsSplitQuery()
                .Include(item => item.Items)
                .Include(item => item.PaymentTransactions)
                .Include(item => item.Shipments)
                .Include(item => item.StatusHistory)
                .SingleOrDefaultAsync(
                    item => item.Id == orderId,
                    cancellationToken);

            if (order is null)
            {
                await databaseTransaction.RollbackAsync(
                    CancellationToken.None);

                return DevelopmentSimulationResult.Failure(
                    "DEV_PAYMENT_ORDER_NOT_FOUND",
                    "Không tìm thấy đơn hàng.");
            }

            if (order.PaymentStatus == PaymentStatus.Paid
                && order.OrderStatus is not OrderStatus.PendingPayment)
            {
                await databaseTransaction.CommitAsync(
                    cancellationToken);

                return DevelopmentSimulationResult.Completed(
                    "DEV_PAYMENT_ALREADY_CONFIRMED",
                    "Đơn hàng đã được xác nhận thanh toán trước đó.",
                    alreadyProcessed: true);
            }

            if (order.OrderStatus != OrderStatus.PendingPayment
                || order.PaymentStatus != PaymentStatus.Pending)
            {
                await databaseTransaction.RollbackAsync(
                    CancellationToken.None);

                return DevelopmentSimulationResult.Failure(
                    "DEV_PAYMENT_STATE_INVALID",
                    $"Chỉ mô phỏng được đơn PendingPayment/Pending. "
                    + $"Hiện tại: {order.OrderStatus}/{order.PaymentStatus}.");
            }

            var payment = order.PaymentTransactions
                .Where(item =>
                    item.Provider == VnPayProvider
                    && item.Status == PaymentStatus.Pending)
                .OrderByDescending(item => item.AttemptNumber)
                .ThenByDescending(item => item.Id)
                .FirstOrDefault();

            if (payment is null)
            {
                await databaseTransaction.RollbackAsync(
                    CancellationToken.None);

                return DevelopmentSimulationResult.Failure(
                    "DEV_VNPAY_TRANSACTION_NOT_FOUND",
                    "Không tìm thấy giao dịch VNPay đang chờ xử lý.");
            }

            if (payment.Amount != order.GrandTotal)
            {
                await databaseTransaction.RollbackAsync(
                    CancellationToken.None);

                return DevelopmentSimulationResult.Failure(
                    "DEV_PAYMENT_AMOUNT_MISMATCH",
                    "Số tiền giao dịch VNPay không khớp tổng tiền đơn hàng.");
            }

            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var normalizedActor = NormalizeActor(actor);
            var correlationId =
                $"dev-payment:{payment.MerchantReference}";

            payment.Status = PaymentStatus.Paid;
            payment.ProviderTransactionId ??=
                $"DEV-{payment.Id}-{nowUtc:yyyyMMddHHmmss}";
            payment.ResponsePayload = JsonSerializer.Serialize(new
            {
                simulated = true,
                environment = _environment.EnvironmentName,
                source = SimulatorActor,
                confirmedBy = normalizedActor,
                confirmedAtUtc = nowUtc,
                merchantReference = payment.MerchantReference,
                amount = payment.Amount,
                currency = payment.Currency
            });
            payment.FailureCode = null;
            payment.FailureMessage = null;
            payment.CompletedAt = nowUtc;
            payment.UpdatedAt = nowUtc;

            order.PaymentStatus = PaymentStatus.Paid;
            order.OrderStatus = OrderStatus.Placed;
            order.FulfillmentStatus = FulfillmentStatus.Unfulfilled;
            order.PlacedAt ??= nowUtc;
            order.CancelledAt = null;
            order.CancelReason = null;
            order.UpdatedAt = nowUtc;

            order.StatusHistory.Add(
                new OrderStatusHistory
                {
                    Category = OrderHistoryCategory.Payment,
                    FromStatus = PaymentStatus.Pending.ToString(),
                    ToStatus = PaymentStatus.Paid.ToString(),
                    Code = "DEV_VNPAY_PAYMENT_CONFIRMED",
                    Title = "Đã mô phỏng thanh toán VNPay thành công",
                    Description =
                        "Giao dịch được xác nhận bằng công cụ Development. "
                        + "Không có tiền thật được chuyển qua VNPay.",
                    ChangedBy = normalizedActor,
                    CustomerVisible = false,
                    OccurredAt = nowUtc,
                    CorrelationId = correlationId
                });

            order.StatusHistory.Add(
                new OrderStatusHistory
                {
                    Category = OrderHistoryCategory.Order,
                    FromStatus = OrderStatus.PendingPayment.ToString(),
                    ToStatus = OrderStatus.Placed.ToString(),
                    Code = "ORDER_PLACED_AFTER_DEV_PAYMENT",
                    Title = "Đơn hàng chuyển sang xử lý",
                    Description =
                        "Đơn được mở khóa sau khi mô phỏng thanh toán thành công.",
                    ChangedBy = normalizedActor,
                    CustomerVisible = false,
                    OccurredAt = nowUtc,
                    CorrelationId = correlationId
                });

            await _inventory.CommitAsync(
                order.Id,
                correlationId,
                SimulatorActor,
                cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);
            await databaseTransaction.CommitAsync(cancellationToken);

            return DevelopmentSimulationResult.Completed(
                "DEV_PAYMENT_CONFIRMED",
                $"Đã mô phỏng thanh toán thành công cho đơn {order.Code}.");
        }
        catch
        {
            await databaseTransaction.RollbackAsync(
                CancellationToken.None);
            throw;
        }
    }

    public async Task<DevelopmentSimulationResult>
        ConfirmVnPayRefundAsync(
            long returnRequestId,
            string actor,
            CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return Disabled();
        }

        if (returnRequestId <= 0)
        {
            return DevelopmentSimulationResult.Failure(
                "DEV_REFUND_REQUEST_INVALID",
                "Mã yêu cầu hoàn trả không hợp lệ.");
        }

        await using var databaseTransaction =
            await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            var request = await _context.ReturnRequests
                .AsSplitQuery()
                .Include(item => item.Items)
                .Include(item => item.Order)
                    .ThenInclude(order => order.PaymentTransactions)
                .Include(item => item.Order)
                    .ThenInclude(order => order.ReturnRequests)
                        .ThenInclude(otherReturn => otherReturn.Items)
                .Include(item => item.Order)
                    .ThenInclude(order => order.StatusHistory)
                .SingleOrDefaultAsync(
                    item => item.Id == returnRequestId,
                    cancellationToken);

            if (request is null)
            {
                await databaseTransaction.RollbackAsync(
                    CancellationToken.None);

                return DevelopmentSimulationResult.Failure(
                    "DEV_REFUND_REQUEST_NOT_FOUND",
                    "Không tìm thấy yêu cầu hoàn trả.");
            }

            if (request.Status is ReturnRequestStatus.Refunded
                or ReturnRequestStatus.Closed)
            {
                await databaseTransaction.CommitAsync(
                    cancellationToken);

                return DevelopmentSimulationResult.Completed(
                    "DEV_REFUND_ALREADY_CONFIRMED",
                    "Yêu cầu đã được xác nhận hoàn tiền trước đó.",
                    alreadyProcessed: true);
            }

            if (request.Status != ReturnRequestStatus.RefundPending)
            {
                await databaseTransaction.RollbackAsync(
                    CancellationToken.None);

                return DevelopmentSimulationResult.Failure(
                    "DEV_REFUND_STATE_INVALID",
                    $"Chỉ mô phỏng hoàn tiền khi yêu cầu ở RefundPending. "
                    + $"Hiện tại: {request.Status}.");
            }

            var payment = request.Order.PaymentTransactions
                .Where(item =>
                    item.Provider == VnPayProvider
                    && item.Status is PaymentStatus.Paid
                        or PaymentStatus.PartiallyRefunded)
                .OrderByDescending(item => item.AttemptNumber)
                .ThenByDescending(item => item.Id)
                .FirstOrDefault();

            if (payment is null)
            {
                await databaseTransaction.RollbackAsync(
                    CancellationToken.None);

                return DevelopmentSimulationResult.Failure(
                    "DEV_REFUND_VNPAY_PAYMENT_NOT_FOUND",
                    "Không tìm thấy giao dịch VNPay đã thanh toán để hoàn tiền.");
            }

            var refundAmount = request.Items.Sum(item => item.RefundAmount);

            if (refundAmount <= 0)
            {
                await databaseTransaction.RollbackAsync(
                    CancellationToken.None);

                return DevelopmentSimulationResult.Failure(
                    "DEV_REFUND_AMOUNT_INVALID",
                    "Yêu cầu không có số tiền hợp lệ để hoàn.");
            }

            var previouslyRefunded = request.Order.ReturnRequests
                .Where(item =>
                    item.Id != request.Id
                    && item.Status is ReturnRequestStatus.Refunded
                        or ReturnRequestStatus.Closed)
                .SelectMany(item => item.Items)
                .Sum(item => item.RefundAmount);

            var totalRefunded = previouslyRefunded + refundAmount;

            if (totalRefunded > request.Order.GrandTotal)
            {
                await databaseTransaction.RollbackAsync(
                    CancellationToken.None);

                return DevelopmentSimulationResult.Failure(
                    "DEV_REFUND_EXCEEDS_ORDER_TOTAL",
                    "Tổng tiền hoàn vượt quá tổng thanh toán của đơn hàng.");
            }

            var targetPaymentStatus =
                totalRefunded >= request.Order.GrandTotal
                    ? PaymentStatus.Refunded
                    : PaymentStatus.PartiallyRefunded;

            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var normalizedActor = NormalizeActor(actor);
            var correlationId = $"dev-refund:{request.Code}";

            var previousPaymentStatus = request.Order.PaymentStatus;

            payment.Status = targetPaymentStatus;
            payment.UpdatedAt = nowUtc;

            request.Order.PaymentStatus = targetPaymentStatus;
            request.Order.UpdatedAt = nowUtc;

            request.Status = ReturnRequestStatus.Refunded;
            request.CompletedAt = nowUtc;
            request.UpdatedAt = nowUtc;

            request.Order.StatusHistory.Add(
                new OrderStatusHistory
                {
                    Category = OrderHistoryCategory.Payment,
                    FromStatus = previousPaymentStatus.ToString(),
                    ToStatus = targetPaymentStatus.ToString(),
                    Code = "DEV_VNPAY_REFUND_CONFIRMED",
                    Title = "Đã mô phỏng hoàn tiền VNPay thành công",
                    Description =
                        $"Đã ghi nhận hoàn {refundAmount:N0} ₫ bằng công cụ "
                        + "Development. Không có tiền thật được VNPay xử lý.",
                    ChangedBy = normalizedActor,
                    CustomerVisible = false,
                    OccurredAt = nowUtc,
                    CorrelationId = correlationId
                });

            request.Order.StatusHistory.Add(
                new OrderStatusHistory
                {
                    Category = OrderHistoryCategory.Return,
                    FromStatus = ReturnRequestStatus.RefundPending.ToString(),
                    ToStatus = ReturnRequestStatus.Refunded.ToString(),
                    Code = "RETURN_REFUNDED_BY_DEV_SIMULATOR",
                    Title = "Yêu cầu hoàn trả đã được hoàn tiền",
                    Description =
                        $"Mã {request.Code}; số tiền {refundAmount:N0} ₫.",
                    ChangedBy = normalizedActor,
                    CustomerVisible = true,
                    OccurredAt = nowUtc,
                    CorrelationId = correlationId
                });

            await _context.SaveChangesAsync(cancellationToken);
            await databaseTransaction.CommitAsync(cancellationToken);

            return DevelopmentSimulationResult.Completed(
                "DEV_REFUND_CONFIRMED",
                $"Đã mô phỏng hoàn {refundAmount:N0} ₫ cho {request.Code}.");
        }
        catch
        {
            await databaseTransaction.RollbackAsync(
                CancellationToken.None);
            throw;
        }
    }

    public async Task<DevelopmentSimulationResult> CloseRefundAsync(
        long returnRequestId,
        string actor,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return Disabled();
        }

        var request = await _context.ReturnRequests
            .Include(item => item.Order)
                .ThenInclude(order => order.StatusHistory)
            .SingleOrDefaultAsync(
                item => item.Id == returnRequestId,
                cancellationToken);

        if (request is null)
        {
            return DevelopmentSimulationResult.Failure(
                "DEV_REFUND_REQUEST_NOT_FOUND",
                "Không tìm thấy yêu cầu hoàn trả.");
        }

        if (request.Status == ReturnRequestStatus.Closed)
        {
            return DevelopmentSimulationResult.Completed(
                "DEV_REFUND_ALREADY_CLOSED",
                "Yêu cầu hoàn trả đã được đóng trước đó.",
                alreadyProcessed: true);
        }

        if (request.Status != ReturnRequestStatus.Refunded)
        {
            return DevelopmentSimulationResult.Failure(
                "DEV_REFUND_CLOSE_STATE_INVALID",
                $"Chỉ đóng yêu cầu ở trạng thái Refunded. "
                + $"Hiện tại: {request.Status}.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var normalizedActor = NormalizeActor(actor);
        var correlationId = $"dev-refund-close:{request.Code}";

        request.Status = ReturnRequestStatus.Closed;
        request.CompletedAt ??= nowUtc;
        request.UpdatedAt = nowUtc;

        request.Order.StatusHistory.Add(
            new OrderStatusHistory
            {
                Category = OrderHistoryCategory.Return,
                FromStatus = ReturnRequestStatus.Refunded.ToString(),
                ToStatus = ReturnRequestStatus.Closed.ToString(),
                Code = "RETURN_CLOSED_AFTER_DEV_REFUND",
                Title = "Yêu cầu hoàn trả đã đóng",
                Description =
                    "Hoàn tiền đã được xác nhận và hồ sơ hoàn trả đã kết thúc.",
                ChangedBy = normalizedActor,
                CustomerVisible = true,
                OccurredAt = nowUtc,
                CorrelationId = correlationId
            });

        await _context.SaveChangesAsync(cancellationToken);

        return DevelopmentSimulationResult.Completed(
            "DEV_REFUND_CLOSED",
            $"Đã đóng yêu cầu hoàn trả {request.Code}.");
    }

    private DevelopmentSimulationResult Disabled() =>
        DevelopmentSimulationResult.Failure(
            "DEV_SIMULATOR_DISABLED",
            "Công cụ mô phỏng chỉ hoạt động trong môi trường Development.");

    private static string NormalizeActor(string? actor)
    {
        var normalized = actor?.Trim();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return SimulatorActor;
        }

        return normalized.Length <= 100
            ? normalized
            : normalized[..100];
    }
}
