using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Commerce.Returns;

namespace WebApplication2.Services.Payments.Refunds;

public sealed record InternalRefundResult(
    long ReturnRequestId,
    decimal Amount,
    string Reference,
    ReturnRequestStatus Status,
    string Message);

public interface IInternalRefundService
{
    Task<InternalRefundResult> CompleteAsync(
        long returnRequestId,
        byte[] rowVersion,
        string actor,
        CancellationToken cancellationToken);

    Task<InternalRefundResult> CloseAsync(
        long returnRequestId,
        byte[] rowVersion,
        string actor,
        CancellationToken cancellationToken);
}

public sealed class InternalRefundService : IInternalRefundService
{
    private const string RefundProvider = "VIETQR";
    private const string RefundMethod = "Refund";

    private readonly ApplicationDbContext _context;
    private readonly IReturnRefundDestinationService _destinationService;
    private readonly TimeProvider _timeProvider;

    public InternalRefundService(
        ApplicationDbContext context,
        IReturnRefundDestinationService destinationService,
        TimeProvider timeProvider)
    {
        _context = context;
        _destinationService = destinationService;
        _timeProvider = timeProvider;
    }

    public async Task<InternalRefundResult> CompleteAsync(
        long returnRequestId,
        byte[] rowVersion,
        string actor,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        try
        {
            var request = await _context.ReturnRequests
                .AsSplitQuery()
                .Include(item => item.Items)
                .Include(item => item.Order)
                    .ThenInclude(order => order.PaymentTransactions)
                .Include(item => item.Order)
                    .ThenInclude(order => order.StatusHistory)
                .SingleOrDefaultAsync(
                    item => item.Id == returnRequestId,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    "Không tìm thấy yêu cầu hoàn trả.");

            if (request.Status == ReturnRequestStatus.Refunded)
            {
                var existing = request.Order.PaymentTransactions
                    .Where(item =>
                        item.Method == RefundMethod
                        && item.Status == PaymentStatus.Refunded
                        && item.IdempotencyKey == BuildIdempotencyKey(request.Id))
                    .OrderByDescending(item => item.Id)
                    .FirstOrDefault();

                await transaction.CommitAsync(cancellationToken);
                return new InternalRefundResult(
                    request.Id,
                    existing?.Amount ?? request.Items.Sum(item => item.RefundAmount),
                    existing?.ProviderTransactionId ?? request.Code,
                    request.Status,
                    "Khoản hoàn tiền đã được ghi nhận trước đó.");
            }

            if (request.Status != ReturnRequestStatus.RefundPending)
            {
                throw new InvalidOperationException(
                    "Yêu cầu chưa sẵn sàng để hoàn tiền.");
            }

            if (rowVersion is not { Length: > 0 })
            {
                throw new InvalidOperationException(
                    "Dữ liệu vừa thay đổi. Vui lòng tải lại trang.");
            }

            _context.Entry(request)
                .Property(item => item.RowVersion)
                .OriginalValue = rowVersion;

            var destination = await _destinationService.GetAsync(
                request.Id,
                cancellationToken)
                ?? throw new InvalidOperationException(
                    "Yêu cầu chưa có tài khoản nhận tiền hoàn.");

            var amount = request.Items.Sum(item => item.RefundAmount);
            if (amount <= 0)
            {
                throw new InvalidOperationException(
                    "Số tiền hoàn chưa được xác định.");
            }

            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var idempotencyKey = BuildIdempotencyKey(request.Id);
            var existingTransaction = request.Order.PaymentTransactions
                .SingleOrDefault(item => item.IdempotencyKey == idempotencyKey);

            if (existingTransaction is null)
            {
                var attempt = request.Order.PaymentTransactions.Count == 0
                    ? 1
                    : request.Order.PaymentTransactions.Max(item => item.AttemptNumber) + 1;
                var reference = $"VQR-{request.Id}-{nowUtc:yyyyMMddHHmmss}";

                existingTransaction = new PaymentTransaction
                {
                    OrderId = request.OrderId,
                    Provider = RefundProvider,
                    Method = RefundMethod,
                    Status = PaymentStatus.Refunded,
                    AttemptNumber = attempt,
                    Amount = amount,
                    Currency = request.Order.Currency,
                    MerchantReference = request.Code,
                    ProviderTransactionId = reference,
                    IdempotencyKey = idempotencyKey,
                    RequestPayload = JsonSerializer.Serialize(new
                    {
                        request.Id,
                        destination.BankBin,
                        destination.BankCode,
                        destination.AccountNumber,
                        destination.AccountName,
                        Amount = amount
                    }),
                    ResponsePayload = JsonSerializer.Serialize(new
                    {
                        Status = "Completed",
                        CompletedAt = nowUtc
                    }),
                    CompletedAt = nowUtc,
                    CreatedAt = nowUtc,
                    UpdatedAt = nowUtc
                };
                request.Order.PaymentTransactions.Add(existingTransaction);
            }
            else
            {
                existingTransaction.Status = PaymentStatus.Refunded;
                existingTransaction.Amount = amount;
                existingTransaction.CompletedAt = nowUtc;
                existingTransaction.UpdatedAt = nowUtc;
            }

            var refundedTotal = request.Order.PaymentTransactions
                .Where(item =>
                    item.Method == RefundMethod
                    && item.Status == PaymentStatus.Refunded)
                .Sum(item => item.Amount);

            request.Order.PaymentStatus = refundedTotal >= request.Order.GrandTotal
                ? PaymentStatus.Refunded
                : PaymentStatus.PartiallyRefunded;
            request.Order.UpdatedAt = nowUtc;

            request.Status = ReturnRequestStatus.Refunded;
            request.CompletedAt = nowUtc;
            request.UpdatedAt = nowUtc;

            request.Order.StatusHistory.Add(new OrderStatusHistory
            {
                Category = OrderHistoryCategory.Payment,
                FromStatus = ReturnRequestStatus.RefundPending.ToString(),
                ToStatus = ReturnRequestStatus.Refunded.ToString(),
                Code = "RETURN_REFUND_COMPLETED",
                Title = "Hoàn tiền đã hoàn tất",
                Description = $"{amount:N0} ₫ đã được chuyển đến tài khoản {destination.MaskedAccountNumber} tại {destination.BankName}.",
                ChangedBy = NormalizeActor(actor),
                CustomerVisible = true,
                OccurredAt = nowUtc,
                CorrelationId = idempotencyKey
            });

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new InternalRefundResult(
                request.Id,
                amount,
                existingTransaction.ProviderTransactionId ?? request.Code,
                request.Status,
                "Đã xác nhận hoàn tiền cho khách hàng.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<InternalRefundResult> CloseAsync(
        long returnRequestId,
        byte[] rowVersion,
        string actor,
        CancellationToken cancellationToken)
    {
        var request = await _context.ReturnRequests
            .Include(item => item.Items)
            .Include(item => item.Order)
                .ThenInclude(order => order.StatusHistory)
            .SingleOrDefaultAsync(
                item => item.Id == returnRequestId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Không tìm thấy yêu cầu hoàn trả.");

        if (request.Status == ReturnRequestStatus.Closed)
        {
            return new InternalRefundResult(
                request.Id,
                request.Items.Sum(item => item.RefundAmount),
                request.Code,
                request.Status,
                "Hồ sơ đã được hoàn tất trước đó.");
        }

        if (request.Status != ReturnRequestStatus.Refunded)
        {
            throw new InvalidOperationException(
                "Chỉ có thể hoàn tất hồ sơ sau khi tiền đã được chuyển.");
        }

        if (rowVersion is not { Length: > 0 })
        {
            throw new InvalidOperationException(
                "Dữ liệu vừa thay đổi. Vui lòng tải lại trang.");
        }

        _context.Entry(request)
            .Property(item => item.RowVersion)
            .OriginalValue = rowVersion;

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        request.Status = ReturnRequestStatus.Closed;
        request.UpdatedAt = nowUtc;
        request.CompletedAt ??= nowUtc;

        request.Order.StatusHistory.Add(new OrderStatusHistory
        {
            Category = OrderHistoryCategory.Return,
            FromStatus = ReturnRequestStatus.Refunded.ToString(),
            ToStatus = ReturnRequestStatus.Closed.ToString(),
            Code = "RETURN_CLOSED",
            Title = "Yêu cầu hoàn trả đã hoàn tất",
            Description = "FastBuy đã hoàn tất tiếp nhận hàng và hoàn tiền.",
            ChangedBy = NormalizeActor(actor),
            CustomerVisible = true,
            OccurredAt = nowUtc,
            CorrelationId = $"return:close:{request.Id}"
        });

        await _context.SaveChangesAsync(cancellationToken);

        return new InternalRefundResult(
            request.Id,
            request.Items.Sum(item => item.RefundAmount),
            request.Code,
            request.Status,
            "Đã hoàn tất hồ sơ hoàn trả.");
    }

    private static string BuildIdempotencyKey(long returnRequestId) =>
        $"refund:return:{returnRequestId}";

    private static string NormalizeActor(string? actor)
    {
        var value = string.IsNullOrWhiteSpace(actor) ? "Admin" : actor.Trim();
        return value.Length <= 100 ? value : value[..100];
    }
}
