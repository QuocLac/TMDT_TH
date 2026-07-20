using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Commerce.Inventory;
using WebApplication2.Services.Payments.Refunds;

namespace WebApplication2.Services.Payments.Simulation;

public sealed class DevelopmentPaymentSimulator
    : IDevelopmentPaymentSimulator
{
    private const string VnPayProvider = "VNPAY";
    private const string SimulatorActor =
        "DevelopmentSimulator";

    private readonly ApplicationDbContext _context;
    private readonly IInventoryService _inventory;
    private readonly IRefundSettlementService
        _refundSettlement;
    private readonly IWebHostEnvironment _environment;
    private readonly TimeProvider _timeProvider;

    public DevelopmentPaymentSimulator(
        ApplicationDbContext context,
        IInventoryService inventory,
        IRefundSettlementService refundSettlement,
        IWebHostEnvironment environment,
        TimeProvider timeProvider)
    {
        _context = context;
        _inventory = inventory;
        _refundSettlement = refundSettlement;
        _environment = environment;
        _timeProvider = timeProvider;
    }

    public bool IsEnabled =>
        _environment.IsDevelopment();

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
                .Include(item =>
                    item.PaymentTransactions)
                .Include(item => item.Shipments)
                .Include(item => item.StatusHistory)
                .SingleOrDefaultAsync(
                    item => item.Id == orderId,
                    cancellationToken);

            if (order is null)
            {
                await databaseTransaction.RollbackAsync(
                    CancellationToken.None);

                return DevelopmentSimulationResult
                    .Failure(
                        "DEV_PAYMENT_ORDER_NOT_FOUND",
                        "Không tìm thấy đơn hàng.");
            }

            if (order.PaymentStatus
                    == PaymentStatus.Paid
                && order.OrderStatus
                    is not OrderStatus.PendingPayment)
            {
                await databaseTransaction.CommitAsync(
                    cancellationToken);

                return DevelopmentSimulationResult
                    .Completed(
                        "DEV_PAYMENT_ALREADY_CONFIRMED",
                        "Đơn hàng đã được xác nhận "
                        + "thanh toán trước đó.",
                        alreadyProcessed: true);
            }

            if (order.OrderStatus
                    != OrderStatus.PendingPayment
                || order.PaymentStatus
                    != PaymentStatus.Pending)
            {
                await databaseTransaction.RollbackAsync(
                    CancellationToken.None);

                return DevelopmentSimulationResult
                    .Failure(
                        "DEV_PAYMENT_STATE_INVALID",
                        "Chỉ mô phỏng được đơn "
                        + "PendingPayment/Pending. "
                        + $"Hiện tại: "
                        + $"{order.OrderStatus}/"
                        + $"{order.PaymentStatus}.");
            }

            var payment =
                order.PaymentTransactions
                    .Where(item =>
                        item.Provider == VnPayProvider
                        && item.Status
                            == PaymentStatus.Pending)
                    .OrderByDescending(item =>
                        item.AttemptNumber)
                    .ThenByDescending(item => item.Id)
                    .FirstOrDefault();

            if (payment is null)
            {
                await databaseTransaction.RollbackAsync(
                    CancellationToken.None);

                return DevelopmentSimulationResult
                    .Failure(
                        "DEV_VNPAY_TRANSACTION_NOT_FOUND",
                        "Không tìm thấy giao dịch VNPay "
                        + "đang chờ xử lý.");
            }

            if (payment.Amount != order.GrandTotal)
            {
                await databaseTransaction.RollbackAsync(
                    CancellationToken.None);

                return DevelopmentSimulationResult
                    .Failure(
                        "DEV_PAYMENT_AMOUNT_MISMATCH",
                        "Số tiền giao dịch VNPay không "
                        + "khớp tổng tiền đơn hàng.");
            }

            var nowUtc =
                _timeProvider.GetUtcNow().UtcDateTime;
            var normalizedActor =
                NormalizeActor(actor);
            var correlationId =
                $"dev-payment:"
                + $"{payment.MerchantReference}";

            payment.Status = PaymentStatus.Paid;
            payment.ProviderTransactionId ??=
                $"DEV-{payment.Id}-"
                + $"{nowUtc:yyyyMMddHHmmss}";
            payment.ResponsePayload =
                JsonSerializer.Serialize(new
                {
                    simulated = true,
                    environment =
                        _environment.EnvironmentName,
                    source = SimulatorActor,
                    confirmedBy = normalizedActor,
                    confirmedAtUtc = nowUtc,
                    merchantReference =
                        payment.MerchantReference,
                    amount = payment.Amount,
                    currency = payment.Currency
                });
            payment.FailureCode = null;
            payment.FailureMessage = null;
            payment.CompletedAt = nowUtc;
            payment.UpdatedAt = nowUtc;

            order.PaymentStatus =
                PaymentStatus.Paid;
            order.OrderStatus =
                OrderStatus.Placed;
            order.FulfillmentStatus =
                FulfillmentStatus.Unfulfilled;
            order.PlacedAt ??= nowUtc;
            order.CancelledAt = null;
            order.CancelReason = null;
            order.UpdatedAt = nowUtc;

            order.StatusHistory.Add(
                new OrderStatusHistory
                {
                    Category =
                        OrderHistoryCategory.Payment,
                    FromStatus =
                        PaymentStatus.Pending.ToString(),
                    ToStatus =
                        PaymentStatus.Paid.ToString(),
                    Code =
                        "DEV_VNPAY_PAYMENT_CONFIRMED",
                    Title =
                        "Đã mô phỏng thanh toán "
                        + "VNPay thành công",
                    Description =
                        "Giao dịch được xác nhận bằng "
                        + "công cụ Development. Không có "
                        + "tiền thật được chuyển qua VNPay.",
                    ChangedBy = normalizedActor,
                    CustomerVisible = false,
                    OccurredAt = nowUtc,
                    CorrelationId = correlationId
                });

            order.StatusHistory.Add(
                new OrderStatusHistory
                {
                    Category =
                        OrderHistoryCategory.Order,
                    FromStatus =
                        OrderStatus.PendingPayment
                            .ToString(),
                    ToStatus =
                        OrderStatus.Placed.ToString(),
                    Code =
                        "ORDER_PLACED_AFTER_DEV_PAYMENT",
                    Title =
                        "Đơn hàng chuyển sang xử lý",
                    Description =
                        "Đơn được mở khóa sau khi mô "
                        + "phỏng thanh toán thành công.",
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

            await _context.SaveChangesAsync(
                cancellationToken);
            await databaseTransaction.CommitAsync(
                cancellationToken);

            return DevelopmentSimulationResult
                .Completed(
                    "DEV_PAYMENT_CONFIRMED",
                    "Đã mô phỏng thanh toán thành "
                    + $"công cho đơn {order.Code}.");
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

        var nowUtc =
            _timeProvider.GetUtcNow().UtcDateTime;
        var refundReference =
            $"DEV-RF-{returnRequestId}";
        var providerTransactionId =
            $"DEV-RF-{returnRequestId}-"
            + $"{nowUtc:yyyyMMddHHmmss}";

        var settlement =
            await _refundSettlement.SettleAsync(
                new RefundSettlementCommand(
                    returnRequestId,
                    VnPayProvider,
                    refundReference,
                    providerTransactionId,
                    RequestPayload:
                        JsonSerializer.Serialize(new
                        {
                            simulated = true,
                            returnRequestId,
                            requestedBy =
                                NormalizeActor(actor),
                            requestedAtUtc = nowUtc
                        }),
                    ResponsePayload:
                        JsonSerializer.Serialize(new
                        {
                            simulated = true,
                            success = true,
                            provider = VnPayProvider,
                            refundReference,
                            providerTransactionId,
                            completedAtUtc = nowUtc
                        }),
                    Actor: NormalizeActor(actor),
                    IsSimulation: true),
                cancellationToken);

        return settlement.Success
            ? DevelopmentSimulationResult.Completed(
                settlement.Code,
                settlement.Message,
                settlement.AlreadyProcessed)
            : DevelopmentSimulationResult.Failure(
                settlement.Code,
                settlement.Message);
    }

    public async Task<DevelopmentSimulationResult>
        CloseRefundAsync(
            long returnRequestId,
            string actor,
            CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return Disabled();
        }

        var result =
            await _refundSettlement.CloseAsync(
                returnRequestId,
                NormalizeActor(actor),
                cancellationToken);

        return result.Success
            ? DevelopmentSimulationResult.Completed(
                result.Code,
                result.Message,
                result.AlreadyProcessed)
            : DevelopmentSimulationResult.Failure(
                result.Code,
                result.Message);
    }

    private DevelopmentSimulationResult Disabled() =>
        DevelopmentSimulationResult.Failure(
            "DEV_SIMULATOR_DISABLED",
            "Công cụ mô phỏng chỉ hoạt động "
            + "trong môi trường Development.");

    private static string NormalizeActor(
        string? actor)
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
