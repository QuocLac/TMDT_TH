using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Models.Enums;

namespace WebApplication2.Services.Payments.Refunds;

public sealed class RefundSettlementService
    : IRefundSettlementService
{
    private const string RefundMethod = "Refund";
    private const string RefundIdempotencyPrefix = "refund:return:";

    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public RefundSettlementService(
        ApplicationDbContext context,
        TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<RefundSettlementResult> SettleAsync(
        RefundSettlementCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.ReturnRequestId <= 0)
        {
            return RefundSettlementResult.Failure(
                "REFUND_REQUEST_INVALID",
                "Mã yêu cầu hoàn trả không hợp lệ.");
        }

        var provider = NormalizeRequired(
            command.Provider,
            50,
            "Nhà cung cấp hoàn tiền");
        var providerReference = NormalizeRequired(
            command.ProviderRefundReference,
            100,
            "Mã tham chiếu hoàn tiền");
        var providerTransactionId = NormalizeOptional(
            command.ProviderTransactionId,
            150);
        var actor = NormalizeRequired(
            command.Actor,
            100,
            "Người xác nhận");
        var idempotencyKey =
            $"{RefundIdempotencyPrefix}{command.ReturnRequestId}";

        await using var transaction =
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
                    item => item.Id == command.ReturnRequestId,
                    cancellationToken);

            if (request is null)
            {
                await transaction.RollbackAsync(
                    CancellationToken.None);

                return RefundSettlementResult.Failure(
                    "REFUND_REQUEST_NOT_FOUND",
                    "Không tìm thấy yêu cầu hoàn trả.");
            }

            var existingRefund = request.Order.PaymentTransactions
                .SingleOrDefault(item =>
                    item.IdempotencyKey == idempotencyKey);

            if (existingRefund is not null)
            {
                if (existingRefund.Status != PaymentStatus.Refunded)
                {
                    await transaction.RollbackAsync(
                        CancellationToken.None);

                    return RefundSettlementResult.Failure(
                        "REFUND_TRANSACTION_INCOMPLETE",
                        "Giao dịch hoàn tiền đã tồn tại nhưng chưa hoàn tất.");
                }

                request.Status = request.Status
                    is ReturnRequestStatus.Refunded
                    or ReturnRequestStatus.Closed
                        ? request.Status
                        : ReturnRequestStatus.Refunded;

                request.CompletedAt ??= existingRefund.CompletedAt;
                request.UpdatedAt =
                    _timeProvider.GetUtcNow().UtcDateTime;

                await _context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return RefundSettlementResult.Completed(
                    "REFUND_ALREADY_SETTLED",
                    "Yêu cầu đã được xác nhận hoàn tiền trước đó.",
                    existingRefund.Amount,
                    existingRefund.MerchantReference,
                    alreadyProcessed: true);
            }

            if (request.Status is ReturnRequestStatus.Refunded
                or ReturnRequestStatus.Closed)
            {
                await transaction.CommitAsync(cancellationToken);

                return RefundSettlementResult.Completed(
                    "REFUND_LEGACY_ALREADY_SETTLED",
                    "Yêu cầu đã có trạng thái hoàn tiền từ dữ liệu trước đó.",
                    request.Items.Sum(item => item.RefundAmount),
                    providerReference,
                    alreadyProcessed: true);
            }

            if (request.Status != ReturnRequestStatus.RefundPending)
            {
                await transaction.RollbackAsync(
                    CancellationToken.None);

                return RefundSettlementResult.Failure(
                    "REFUND_STATE_INVALID",
                    $"Chỉ xác nhận hoàn tiền khi yêu cầu ở RefundPending. "
                    + $"Hiện tại: {request.Status}.");
            }

            var originalPayment = request.Order.PaymentTransactions
                .Where(item =>
                    item.Provider == provider
                    && !string.Equals(
                        item.Method,
                        RefundMethod,
                        StringComparison.OrdinalIgnoreCase)
                    && item.Status is (
                        PaymentStatus.Paid
                        or PaymentStatus.PartiallyRefunded
                        or PaymentStatus.Refunded))
                .OrderByDescending(item => item.AttemptNumber)
                .ThenByDescending(item => item.Id)
                .FirstOrDefault();

            if (originalPayment is null)
            {
                await transaction.RollbackAsync(
                    CancellationToken.None);

                return RefundSettlementResult.Failure(
                    "REFUND_ORIGINAL_PAYMENT_NOT_FOUND",
                    $"Không tìm thấy giao dịch {provider} đã thanh toán.");
            }

            var refundAmount = request.Items.Sum(
                item => item.RefundAmount);

            if (refundAmount <= 0)
            {
                await transaction.RollbackAsync(
                    CancellationToken.None);

                return RefundSettlementResult.Failure(
                    "REFUND_AMOUNT_INVALID",
                    "Yêu cầu không có số tiền hợp lệ để hoàn.");
            }

            var settledRefundTransactions =
                request.Order.PaymentTransactions
                    .Where(item =>
                        string.Equals(
                            item.Method,
                            RefundMethod,
                            StringComparison.OrdinalIgnoreCase)
                        && item.Status == PaymentStatus.Refunded)
                    .ToArray();

            var settledReturnIds = settledRefundTransactions
                .Select(item => ParseReturnRequestId(
                    item.IdempotencyKey))
                .Where(item => item.HasValue)
                .Select(item => item!.Value)
                .ToHashSet();

            var dedicatedRefundTotal =
                settledRefundTransactions.Sum(
                    item => item.Amount);

            var legacyRefundTotal = request.Order.ReturnRequests
                .Where(item =>
                    item.Id != request.Id
                    && item.Status is (
                        ReturnRequestStatus.Refunded
                        or ReturnRequestStatus.Closed)
                    && !settledReturnIds.Contains(item.Id))
                .SelectMany(item => item.Items)
                .Sum(item => item.RefundAmount);

            var totalAfterSettlement =
                dedicatedRefundTotal
                + legacyRefundTotal
                + refundAmount;

            var maximumRefundable = Math.Min(
                originalPayment.Amount,
                request.Order.GrandTotal);

            if (totalAfterSettlement > maximumRefundable)
            {
                await transaction.RollbackAsync(
                    CancellationToken.None);

                return RefundSettlementResult.Failure(
                    "REFUND_EXCEEDS_PAYMENT",
                    "Tổng tiền hoàn vượt quá số tiền đã thanh toán.");
            }

            if (providerTransactionId is not null)
            {
                var duplicateProviderTransaction =
                    request.Order.PaymentTransactions.Any(item =>
                        item.Provider == provider
                        && item.ProviderTransactionId
                            == providerTransactionId);

                if (duplicateProviderTransaction)
                {
                    await transaction.RollbackAsync(
                        CancellationToken.None);

                    return RefundSettlementResult.Failure(
                        "REFUND_PROVIDER_TRANSACTION_DUPLICATED",
                        "Mã giao dịch hoàn tiền của nhà cung cấp đã tồn tại.");
                }
            }

            var nowUtc =
                _timeProvider.GetUtcNow().UtcDateTime;
            var previousPaymentStatus =
                request.Order.PaymentStatus;
            var targetPaymentStatus =
                totalAfterSettlement >= maximumRefundable
                    ? PaymentStatus.Refunded
                    : PaymentStatus.PartiallyRefunded;
            var correlationId =
                $"refund:{provider}:{request.Code}";
            var nextAttemptNumber =
                request.Order.PaymentTransactions.Count == 0
                    ? 1
                    : request.Order.PaymentTransactions.Max(
                        item => item.AttemptNumber) + 1;

            var refundTransaction = new PaymentTransaction
            {
                OrderId = request.OrderId,
                Provider = provider,
                Method = RefundMethod,
                Status = PaymentStatus.Refunded,
                AttemptNumber = nextAttemptNumber,
                Amount = refundAmount,
                Currency = request.Order.Currency,
                MerchantReference = providerReference,
                ProviderTransactionId = providerTransactionId,
                IdempotencyKey = idempotencyKey,
                RequestPayload = command.RequestPayload
                    ?? JsonSerializer.Serialize(new
                    {
                        returnRequestId = request.Id,
                        returnCode = request.Code,
                        originalPaymentId = originalPayment.Id,
                        originalMerchantReference =
                            originalPayment.MerchantReference,
                        amount = refundAmount,
                        currency = request.Order.Currency,
                        simulated = command.IsSimulation
                    }),
                ResponsePayload = command.ResponsePayload,
                CompletedAt = nowUtc,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            };

            request.Order.PaymentTransactions.Add(
                refundTransaction);

            request.Order.PaymentStatus =
                targetPaymentStatus;
            request.Order.UpdatedAt = nowUtc;

            request.Status =
                ReturnRequestStatus.Refunded;
            request.CompletedAt = nowUtc;
            request.UpdatedAt = nowUtc;

            request.Order.StatusHistory.Add(
                new OrderStatusHistory
                {
                    Category =
                        OrderHistoryCategory.Payment,
                    FromStatus =
                        previousPaymentStatus.ToString(),
                    ToStatus =
                        targetPaymentStatus.ToString(),
                    Code = command.IsSimulation
                        ? "DEV_VNPAY_REFUND_SETTLED"
                        : "VNPAY_REFUND_SETTLED",
                    Title = command.IsSimulation
                        ? "Đã mô phỏng hoàn tiền VNPay thành công"
                        : "VNPay đã xác nhận hoàn tiền",
                    Description =
                        $"Đã ghi nhận hoàn {refundAmount:N0} ₫. "
                        + (command.IsSimulation
                            ? "Không có tiền thật được xử lý."
                            : $"Mã tham chiếu {providerReference}."),
                    ChangedBy = actor,
                    CustomerVisible =
                        !command.IsSimulation,
                    OccurredAt = nowUtc,
                    CorrelationId = correlationId
                });

            request.Order.StatusHistory.Add(
                new OrderStatusHistory
                {
                    Category =
                        OrderHistoryCategory.Return,
                    FromStatus =
                        ReturnRequestStatus
                            .RefundPending
                            .ToString(),
                    ToStatus =
                        ReturnRequestStatus
                            .Refunded
                            .ToString(),
                    Code = command.IsSimulation
                        ? "DEV_RETURN_REFUND_CONFIRMED"
                        : "RETURN_REFUND_CONFIRMED",
                    Title = command.IsSimulation
                        ? "Đã mô phỏng hoàn tiền cho yêu cầu hoàn trả"
                        : "Yêu cầu hoàn trả đã được hoàn tiền",
                    Description =
                        $"Mã {request.Code}; số tiền "
                        + $"{refundAmount:N0} ₫; tham chiếu "
                        + $"{providerReference}. "
                        + (command.IsSimulation
                            ? "Không có tiền thật được xử lý."
                            : string.Empty),
                    ChangedBy = actor,
                    CustomerVisible = !command.IsSimulation,
                    OccurredAt = nowUtc,
                    CorrelationId = correlationId
                });

            await _context.SaveChangesAsync(
                cancellationToken);
            await transaction.CommitAsync(
                cancellationToken);

            return RefundSettlementResult.Completed(
                "REFUND_SETTLED",
                $"Đã xác nhận hoàn {refundAmount:N0} ₫ "
                + $"cho {request.Code}.",
                refundAmount,
                providerReference);
        }
        catch
        {
            await transaction.RollbackAsync(
                CancellationToken.None);
            throw;
        }
    }

    public async Task<RefundSettlementResult> CloseAsync(
        long returnRequestId,
        string actor,
        CancellationToken cancellationToken)
    {
        if (returnRequestId <= 0)
        {
            return RefundSettlementResult.Failure(
                "REFUND_REQUEST_INVALID",
                "Mã yêu cầu hoàn trả không hợp lệ.");
        }

        actor = NormalizeRequired(
            actor,
            100,
            "Người đóng hồ sơ");

        await using var transaction =
            await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            var request = await _context.ReturnRequests
                .Include(item => item.Items)
                .Include(item => item.Order)
                    .ThenInclude(order =>
                        order.StatusHistory)
                .Include(item => item.Order)
                    .ThenInclude(order =>
                        order.PaymentTransactions)
                .SingleOrDefaultAsync(
                    item => item.Id == returnRequestId,
                    cancellationToken);

            if (request is null)
            {
                await transaction.RollbackAsync(
                    CancellationToken.None);

                return RefundSettlementResult.Failure(
                    "REFUND_REQUEST_NOT_FOUND",
                    "Không tìm thấy yêu cầu hoàn trả.");
            }

            if (request.Status
                == ReturnRequestStatus.Closed)
            {
                await transaction.CommitAsync(
                    cancellationToken);

                return RefundSettlementResult.Completed(
                    "REFUND_ALREADY_CLOSED",
                    "Yêu cầu hoàn trả đã được đóng trước đó.",
                    request.Items.Sum(
                        item => item.RefundAmount),
                    request.Code,
                    alreadyProcessed: true);
            }

            if (request.Status
                != ReturnRequestStatus.Refunded)
            {
                await transaction.RollbackAsync(
                    CancellationToken.None);

                return RefundSettlementResult.Failure(
                    "REFUND_CLOSE_STATE_INVALID",
                    $"Chỉ đóng yêu cầu ở trạng thái Refunded. "
                    + $"Hiện tại: {request.Status}.");
            }

            var refundTransaction =
                request.Order.PaymentTransactions
                    .SingleOrDefault(item =>
                        item.IdempotencyKey
                        == $"{RefundIdempotencyPrefix}"
                           + $"{request.Id}");

            if (refundTransaction is not null
                && refundTransaction.Status
                    != PaymentStatus.Refunded)
            {
                await transaction.RollbackAsync(
                    CancellationToken.None);

                return RefundSettlementResult.Failure(
                    "REFUND_TRANSACTION_NOT_COMPLETED",
                    "Giao dịch hoàn tiền chưa hoàn tất.");
            }

            var nowUtc =
                _timeProvider.GetUtcNow().UtcDateTime;
            var correlationId =
                $"refund-close:{request.Code}";

            request.Status =
                ReturnRequestStatus.Closed;
            request.CompletedAt ??= nowUtc;
            request.UpdatedAt = nowUtc;

            request.Order.StatusHistory.Add(
                new OrderStatusHistory
                {
                    Category =
                        OrderHistoryCategory.Return,
                    FromStatus =
                        ReturnRequestStatus
                            .Refunded
                            .ToString(),
                    ToStatus =
                        ReturnRequestStatus
                            .Closed
                            .ToString(),
                    Code =
                        "RETURN_CLOSED_AFTER_REFUND",
                    Title =
                        "Yêu cầu hoàn trả đã đóng",
                    Description =
                        "Hoàn tiền đã được xác nhận và hồ sơ "
                        + "hoàn trả đã kết thúc.",
                    ChangedBy = actor,
                    CustomerVisible = true,
                    OccurredAt = nowUtc,
                    CorrelationId = correlationId
                });

            await _context.SaveChangesAsync(
                cancellationToken);
            await transaction.CommitAsync(
                cancellationToken);

            return RefundSettlementResult.Completed(
                "REFUND_CLOSED",
                $"Đã đóng yêu cầu hoàn trả "
                + $"{request.Code}.",
                request.Items.Sum(
                    item => item.RefundAmount),
                refundTransaction?.MerchantReference
                    ?? request.Code);
        }
        catch
        {
            await transaction.RollbackAsync(
                CancellationToken.None);
            throw;
        }
    }

    private static long? ParseReturnRequestId(
        string idempotencyKey)
    {
        if (!idempotencyKey.StartsWith(
                RefundIdempotencyPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return long.TryParse(
            idempotencyKey[
                RefundIdempotencyPrefix.Length..],
            out var returnRequestId)
                ? returnRequestId
                : null;
    }

    private static string NormalizeRequired(
        string? value,
        int maximumLength,
        string fieldName)
    {
        var normalized = value?.Trim();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException(
                $"{fieldName} là bắt buộc.");
        }

        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength];
    }

    private static string? NormalizeOptional(
        string? value,
        int maximumLength)
    {
        var normalized = value?.Trim();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength];
    }
}
