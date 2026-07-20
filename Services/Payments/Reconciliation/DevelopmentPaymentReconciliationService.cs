using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Commerce.Inventory;

namespace WebApplication2.Services.Payments.Reconciliation;

public sealed class DevelopmentPaymentReconciliationService
    : IDevelopmentPaymentReconciliationService
{
    private const string VnPayProvider = "VNPAY";
    private const string RefundMethod = "Refund";
    private const string RefundIdempotencyPrefix = "refund:return:";
    private const int MaximumScanItems = 250;

    private readonly ApplicationDbContext _context;
    private readonly IInventoryService _inventory;
    private readonly IWebHostEnvironment _environment;
    private readonly TimeProvider _timeProvider;

    public DevelopmentPaymentReconciliationService(
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

    public async Task<IReadOnlyList<DevelopmentPaymentIssueSnapshot>>
        ScanAsync(CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return [];
        }

        var orders = await _context.Orders
            .AsNoTracking()
            .AsSplitQuery()
            .Where(order => order.PaymentTransactions.Any(payment =>
                payment.Provider == VnPayProvider))
            .Include(order => order.PaymentTransactions)
            .Include(order => order.StockReservations)
            .Include(order => order.ReturnRequests)
                .ThenInclude(request => request.Items)
            .OrderByDescending(order => order.CreatedAt)
            .Take(MaximumScanItems)
            .ToArrayAsync(cancellationToken);

        var returns = await _context.ReturnRequests
            .AsNoTracking()
            .AsSplitQuery()
            .Where(request =>
                request.Status == ReturnRequestStatus.RefundPending
                || request.Status == ReturnRequestStatus.Refunded
                || request.Status == ReturnRequestStatus.Closed)
            .Where(request => request.Order.PaymentTransactions.Any(payment =>
                payment.Provider == VnPayProvider))
            .Include(request => request.Items)
            .Include(request => request.Order)
                .ThenInclude(order => order.PaymentTransactions)
            .OrderByDescending(request => request.RequestedAt)
            .Take(MaximumScanItems)
            .ToArrayAsync(cancellationToken);

        var issues = new List<DevelopmentPaymentIssueSnapshot>();

        foreach (var order in orders)
        {
            var paidCharge = FindPaidCharge(order);

            if (paidCharge is not null
                && (order.OrderStatus == OrderStatus.PendingPayment
                    || order.PaymentStatus == PaymentStatus.Pending))
            {
                var releasedReservation = order.StockReservations.Any(item =>
                    item.Status is StockReservationStatus.Released
                        or StockReservationStatus.Expired);

                issues.Add(new DevelopmentPaymentIssueSnapshot(
                    DevelopmentPaymentIssueType.PaidTransactionOrderPending,
                    "Giao dịch đã Paid nhưng đơn vẫn chờ",
                    releasedReservation
                        ? "Đã có giao dịch VNPay Paid nhưng giữ kho đã bị giải phóng; cần kiểm tra tồn kho trước khi sửa."
                        : "Callback đã ghi nhận giao dịch Paid nhưng trạng thái đơn chưa được mở khóa.",
                    order.Id,
                    order.Code,
                    null,
                    null,
                    paidCharge.Amount,
                    !releasedReservation));
            }

            if (paidCharge is not null)
            {
                var expected = CalculateExpectedPaymentStatus(order);
                if (order.PaymentStatus != expected
                    && order.PaymentStatus != PaymentStatus.Pending)
                {
                    issues.Add(new DevelopmentPaymentIssueSnapshot(
                        DevelopmentPaymentIssueType.OrderPaymentStatusMismatch,
                        "Tổng hoàn tiền không khớp trạng thái đơn",
                        $"Trạng thái hiện tại {order.PaymentStatus}; trạng thái tính từ sổ giao dịch là {expected}.",
                        order.Id,
                        order.Code,
                        null,
                        null,
                        CalculateRefundedAmount(order),
                        true));
                }
            }
        }

        foreach (var request in returns)
        {
            var ledger = FindRefundLedger(
                request.Order.PaymentTransactions,
                request.Id);

            if (request.Status == ReturnRequestStatus.RefundPending
                && ledger is not null
                && ledger.Status == PaymentStatus.Refunded)
            {
                issues.Add(new DevelopmentPaymentIssueSnapshot(
                    DevelopmentPaymentIssueType.RefundLedgerReturnPending,
                    "Đã có bút toán hoàn nhưng yêu cầu vẫn Pending",
                    "Giao dịch hoàn tiền đã hoàn tất nhưng ReturnRequest chưa chuyển sang Refunded.",
                    request.OrderId,
                    request.Order.Code,
                    request.Id,
                    request.Code,
                    ledger.Amount,
                    true));
            }

            if (request.Status is ReturnRequestStatus.Refunded
                    or ReturnRequestStatus.Closed
                && ledger is null)
            {
                issues.Add(new DevelopmentPaymentIssueSnapshot(
                    DevelopmentPaymentIssueType.LegacyRefundLedgerMissing,
                    "Yêu cầu đã hoàn nhưng thiếu bút toán",
                    "Dữ liệu cũ đã ở Refunded/Closed nhưng chưa có PaymentTransaction loại Refund riêng.",
                    request.OrderId,
                    request.Order.Code,
                    request.Id,
                    request.Code,
                    request.Items.Sum(item => item.RefundAmount),
                    false));
            }
        }

        return issues
            .OrderBy(item => item.IsSafeAutomaticRepair ? 0 : 1)
            .ThenBy(item => item.Type)
            .ThenBy(item => item.OrderId)
            .ToArray();
    }

    public async Task<DevelopmentPaymentRepairResult>
        RepairPaidOrderAsync(
            int orderId,
            string actor,
            CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return Disabled();
        }

        await using var transaction =
            await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            var order = await _context.Orders
                .AsSplitQuery()
                .Include(item => item.PaymentTransactions)
                .Include(item => item.StockReservations)
                .Include(item => item.StatusHistory)
                .SingleOrDefaultAsync(
                    item => item.Id == orderId,
                    cancellationToken);

            if (order is null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return DevelopmentPaymentRepairResult.Failure(
                    "PAYMENT_REPAIR_ORDER_NOT_FOUND",
                    "Không tìm thấy đơn hàng.");
            }

            var paidCharge = FindPaidCharge(order);
            if (paidCharge is null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return DevelopmentPaymentRepairResult.Failure(
                    "PAYMENT_REPAIR_PAID_TRANSACTION_NOT_FOUND",
                    "Không tìm thấy giao dịch VNPay Paid.");
            }

            if (order.OrderStatus is OrderStatus.Cancelled
                or OrderStatus.Closed)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return DevelopmentPaymentRepairResult.Failure(
                    "PAYMENT_REPAIR_ORDER_TERMINAL",
                    "Không tự động mở lại đơn đã hủy hoặc đã đóng.");
            }

            if (order.StockReservations.Any(item =>
                    item.Status is StockReservationStatus.Released
                        or StockReservationStatus.Expired))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return DevelopmentPaymentRepairResult.Failure(
                    "PAYMENT_REPAIR_RESERVATION_RELEASED",
                    "Giữ kho đã được giải phóng hoặc hết hạn. Cần kiểm tra tồn kho thủ công trước khi mở đơn.");
            }

            if (order.PaymentStatus == PaymentStatus.Paid
                && order.OrderStatus is not OrderStatus.PendingPayment)
            {
                await transaction.CommitAsync(cancellationToken);
                return DevelopmentPaymentRepairResult.Completed(
                    "PAYMENT_REPAIR_ALREADY_CONSISTENT",
                    "Đơn hàng đã nhất quán.",
                    alreadyConsistent: true);
            }

            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var normalizedActor = NormalizeActor(actor);
            var previousOrderStatus = order.OrderStatus;
            var previousPaymentStatus = order.PaymentStatus;
            var correlationId = $"dev-reconcile-payment:{order.Id}";

            order.PaymentStatus = PaymentStatus.Paid;
            if (order.OrderStatus == OrderStatus.PendingPayment)
            {
                order.OrderStatus = OrderStatus.Placed;
                order.PlacedAt ??= nowUtc;
            }

            order.UpdatedAt = nowUtc;

            await _inventory.CommitAsync(
                order.Id,
                correlationId,
                "DevelopmentReconciliation",
                cancellationToken);

            order.StatusHistory.Add(new OrderStatusHistory
            {
                Category = OrderHistoryCategory.Payment,
                FromStatus = previousPaymentStatus.ToString(),
                ToStatus = PaymentStatus.Paid.ToString(),
                Code = "DEV_PAYMENT_STATE_RECONCILED",
                Title = "Đã đồng bộ trạng thái thanh toán",
                Description =
                    $"Giao dịch {paidCharge.MerchantReference} đã Paid; hệ thống sửa trạng thái đơn bị treo.",
                ChangedBy = normalizedActor,
                CustomerVisible = false,
                OccurredAt = nowUtc,
                CorrelationId = correlationId
            });

            if (previousOrderStatus != order.OrderStatus)
            {
                order.StatusHistory.Add(new OrderStatusHistory
                {
                    Category = OrderHistoryCategory.Order,
                    FromStatus = previousOrderStatus.ToString(),
                    ToStatus = order.OrderStatus.ToString(),
                    Code = "DEV_ORDER_STATE_RECONCILED",
                    Title = "Đã mở khóa đơn sau thanh toán",
                    Description =
                        "Đơn được chuyển sang Placed sau khi đối chiếu giao dịch VNPay Paid.",
                    ChangedBy = normalizedActor,
                    CustomerVisible = false,
                    OccurredAt = nowUtc,
                    CorrelationId = correlationId
                });
            }

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _context.ChangeTracker.Clear();

            return DevelopmentPaymentRepairResult.Completed(
                "PAYMENT_REPAIR_COMPLETED",
                $"Đã sửa trạng thái đơn {order.Code}.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<DevelopmentPaymentRepairResult>
        RepairRefundStateAsync(
            long returnRequestId,
            string actor,
            CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return Disabled();
        }

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
                        .ThenInclude(other => other.Items)
                .Include(item => item.Order)
                    .ThenInclude(order => order.StatusHistory)
                .SingleOrDefaultAsync(
                    item => item.Id == returnRequestId,
                    cancellationToken);

            if (request is null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return DevelopmentPaymentRepairResult.Failure(
                    "REFUND_REPAIR_REQUEST_NOT_FOUND",
                    "Không tìm thấy yêu cầu hoàn trả.");
            }

            var ledger = FindRefundLedger(
                request.Order.PaymentTransactions,
                request.Id);

            if (ledger is null
                || ledger.Status != PaymentStatus.Refunded)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return DevelopmentPaymentRepairResult.Failure(
                    "REFUND_REPAIR_LEDGER_NOT_FOUND",
                    "Chưa có giao dịch hoàn tiền hoàn tất để đối chiếu.");
            }

            if (request.Status is ReturnRequestStatus.Refunded
                or ReturnRequestStatus.Closed)
            {
                await transaction.CommitAsync(cancellationToken);
                return DevelopmentPaymentRepairResult.Completed(
                    "REFUND_REPAIR_ALREADY_CONSISTENT",
                    "Yêu cầu hoàn trả đã nhất quán.",
                    alreadyConsistent: true);
            }

            if (request.Status != ReturnRequestStatus.RefundPending)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return DevelopmentPaymentRepairResult.Failure(
                    "REFUND_REPAIR_STATE_INVALID",
                    $"Không thể tự sửa từ trạng thái {request.Status}.");
            }

            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var normalizedActor = NormalizeActor(actor);
            var correlationId = $"dev-reconcile-refund:{request.Id}";

            request.Status = ReturnRequestStatus.Refunded;
            request.CompletedAt ??= ledger.CompletedAt ?? nowUtc;
            request.UpdatedAt = nowUtc;

            request.Order.PaymentStatus =
                CalculateExpectedPaymentStatus(request.Order);
            request.Order.UpdatedAt = nowUtc;

            request.Order.StatusHistory.Add(new OrderStatusHistory
            {
                Category = OrderHistoryCategory.Return,
                FromStatus = ReturnRequestStatus.RefundPending.ToString(),
                ToStatus = ReturnRequestStatus.Refunded.ToString(),
                Code = "DEV_REFUND_STATE_RECONCILED",
                Title = "Đã đồng bộ trạng thái hoàn tiền",
                Description =
                    $"Đã đối chiếu bút toán {ledger.MerchantReference} và chuyển {request.Code} sang Refunded.",
                ChangedBy = normalizedActor,
                CustomerVisible = false,
                OccurredAt = nowUtc,
                CorrelationId = correlationId
            });

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _context.ChangeTracker.Clear();

            return DevelopmentPaymentRepairResult.Completed(
                "REFUND_REPAIR_COMPLETED",
                $"Đã sửa trạng thái yêu cầu {request.Code}.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<DevelopmentPaymentRepairResult>
        BackfillLegacyRefundLedgerAsync(
            long returnRequestId,
            string actor,
            CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return Disabled();
        }

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
                        .ThenInclude(other => other.Items)
                .Include(item => item.Order)
                    .ThenInclude(order => order.StatusHistory)
                .SingleOrDefaultAsync(
                    item => item.Id == returnRequestId,
                    cancellationToken);

            if (request is null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return DevelopmentPaymentRepairResult.Failure(
                    "REFUND_BACKFILL_REQUEST_NOT_FOUND",
                    "Không tìm thấy yêu cầu hoàn trả.");
            }

            if (request.Status is not (
                    ReturnRequestStatus.Refunded
                    or ReturnRequestStatus.Closed))
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return DevelopmentPaymentRepairResult.Failure(
                    "REFUND_BACKFILL_STATE_INVALID",
                    "Chỉ tạo bút toán lịch sử cho yêu cầu Refunded hoặc Closed.");
            }

            var existing = FindRefundLedger(
                request.Order.PaymentTransactions,
                request.Id);

            if (existing is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return DevelopmentPaymentRepairResult.Completed(
                    "REFUND_BACKFILL_ALREADY_EXISTS",
                    "Bút toán hoàn tiền đã tồn tại.",
                    alreadyConsistent: true);
            }

            var originalPayment = FindPaidCharge(request.Order);
            if (originalPayment is null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return DevelopmentPaymentRepairResult.Failure(
                    "REFUND_BACKFILL_ORIGINAL_PAYMENT_NOT_FOUND",
                    "Không tìm thấy giao dịch VNPay gốc đã thanh toán.");
            }

            var amount = request.Items.Sum(item => item.RefundAmount);
            if (amount <= 0)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return DevelopmentPaymentRepairResult.Failure(
                    "REFUND_BACKFILL_AMOUNT_INVALID",
                    "Yêu cầu không có số tiền hoàn hợp lệ.");
            }

            var currentRefunded = CalculateTotalRefundedAmount(
                request.Order);
            if (currentRefunded + amount > originalPayment.Amount)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return DevelopmentPaymentRepairResult.Failure(
                    "REFUND_BACKFILL_EXCEEDS_PAYMENT",
                    "Tạo bút toán sẽ làm tổng tiền hoàn vượt giao dịch gốc.");
            }

            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var normalizedActor = NormalizeActor(actor);
            var idempotencyKey =
                $"{RefundIdempotencyPrefix}{request.Id}";
            var reference = $"LEGACY-RF-{request.Code}";
            var attempt = request.Order.PaymentTransactions.Count == 0
                ? 1
                : request.Order.PaymentTransactions.Max(
                    item => item.AttemptNumber) + 1;

            var previousPaymentStatus = request.Order.PaymentStatus;

            request.Order.PaymentTransactions.Add(new PaymentTransaction
            {
                OrderId = request.OrderId,
                Provider = VnPayProvider,
                Method = RefundMethod,
                Status = PaymentStatus.Refunded,
                AttemptNumber = attempt,
                Amount = amount,
                Currency = request.Order.Currency,
                MerchantReference = reference,
                ProviderTransactionId = null,
                IdempotencyKey = idempotencyKey,
                RequestPayload = JsonSerializer.Serialize(new
                {
                    legacyBackfill = true,
                    returnRequestId = request.Id,
                    returnCode = request.Code,
                    originalPaymentId = originalPayment.Id,
                    createdBy = normalizedActor
                }),
                ResponsePayload = JsonSerializer.Serialize(new
                {
                    legacyBackfill = true,
                    noProviderCallback = true,
                    completedAtUtc =
                        request.CompletedAt ?? nowUtc
                }),
                CompletedAt = request.CompletedAt ?? nowUtc,
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            });

            request.Order.PaymentStatus =
                CalculateExpectedPaymentStatus(request.Order);
            request.Order.UpdatedAt = nowUtc;

            request.Order.StatusHistory.Add(new OrderStatusHistory
            {
                Category = OrderHistoryCategory.Payment,
                FromStatus = previousPaymentStatus.ToString(),
                ToStatus = request.Order.PaymentStatus.ToString(),
                Code = "DEV_LEGACY_REFUND_LEDGER_BACKFILLED",
                Title = "Đã bổ sung bút toán hoàn tiền lịch sử",
                Description =
                    $"Tạo bút toán {reference} cho dữ liệu hoàn tiền cũ; không gọi VNPay.",
                ChangedBy = normalizedActor,
                CustomerVisible = false,
                OccurredAt = nowUtc,
                CorrelationId = $"dev-backfill-refund:{request.Id}"
            });

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _context.ChangeTracker.Clear();

            return DevelopmentPaymentRepairResult.Completed(
                "REFUND_BACKFILL_COMPLETED",
                $"Đã tạo bút toán lịch sử cho {request.Code}.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<DevelopmentPaymentRepairResult>
        RepairOrderPaymentStatusAsync(
            int orderId,
            string actor,
            CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return Disabled();
        }

        await using var transaction =
            await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            var order = await _context.Orders
                .AsSplitQuery()
                .Include(item => item.PaymentTransactions)
                .Include(item => item.ReturnRequests)
                    .ThenInclude(request => request.Items)
                .Include(item => item.StatusHistory)
                .SingleOrDefaultAsync(
                    item => item.Id == orderId,
                    cancellationToken);

            if (order is null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return DevelopmentPaymentRepairResult.Failure(
                    "PAYMENT_STATUS_REPAIR_ORDER_NOT_FOUND",
                    "Không tìm thấy đơn hàng.");
            }

            if (FindPaidCharge(order) is null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return DevelopmentPaymentRepairResult.Failure(
                    "PAYMENT_STATUS_REPAIR_CHARGE_NOT_FOUND",
                    "Không tìm thấy giao dịch VNPay gốc đã thanh toán.");
            }

            var expected = CalculateExpectedPaymentStatus(order);
            if (order.PaymentStatus == expected)
            {
                await transaction.CommitAsync(cancellationToken);
                return DevelopmentPaymentRepairResult.Completed(
                    "PAYMENT_STATUS_ALREADY_CONSISTENT",
                    "Trạng thái thanh toán đã đúng.",
                    alreadyConsistent: true);
            }

            var previous = order.PaymentStatus;
            var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var normalizedActor = NormalizeActor(actor);

            order.PaymentStatus = expected;
            order.UpdatedAt = nowUtc;
            order.StatusHistory.Add(new OrderStatusHistory
            {
                Category = OrderHistoryCategory.Payment,
                FromStatus = previous.ToString(),
                ToStatus = expected.ToString(),
                Code = "DEV_PAYMENT_TOTAL_RECONCILED",
                Title = "Đã đối chiếu tổng tiền hoàn",
                Description =
                    $"Tổng hoàn đã ghi nhận: {CalculateRefundedAmount(order):N0} ₫.",
                ChangedBy = normalizedActor,
                CustomerVisible = false,
                OccurredAt = nowUtc,
                CorrelationId = $"dev-reconcile-total:{order.Id}"
            });

            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _context.ChangeTracker.Clear();

            return DevelopmentPaymentRepairResult.Completed(
                "PAYMENT_STATUS_REPAIR_COMPLETED",
                $"Đã sửa trạng thái thanh toán của {order.Code} thành {expected}.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<DevelopmentPaymentRepairBatchResult>
        RepairSafeIssuesAsync(
            string actor,
            CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return new DevelopmentPaymentRepairBatchResult(
                0,
                0,
                1,
                ["Công cụ chỉ hoạt động trong Development."]);
        }

        var issues = (await ScanAsync(cancellationToken))
            .Where(item => item.IsSafeAutomaticRepair)
            .Take(100)
            .ToArray();

        var repaired = 0;
        var failures = new List<string>();

        foreach (var issue in issues)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var result = issue.Type switch
                {
                    DevelopmentPaymentIssueType
                        .PaidTransactionOrderPending =>
                        await RepairPaidOrderAsync(
                            issue.OrderId,
                            actor,
                            cancellationToken),

                    DevelopmentPaymentIssueType
                        .RefundLedgerReturnPending =>
                        await RepairRefundStateAsync(
                            issue.ReturnRequestId!.Value,
                            actor,
                            cancellationToken),

                    DevelopmentPaymentIssueType
                        .OrderPaymentStatusMismatch =>
                        await RepairOrderPaymentStatusAsync(
                            issue.OrderId,
                            actor,
                            cancellationToken),

                    _ => DevelopmentPaymentRepairResult.Failure(
                        "PAYMENT_REPAIR_UNSUPPORTED",
                        "Loại sai lệch không hỗ trợ sửa hàng loạt.")
                };

                if (result.Success)
                {
                    repaired++;
                }
                else
                {
                    failures.Add(
                        $"{issue.OrderCode}: {result.Message}");
                }
            }
            catch (Exception exception)
            {
                _context.ChangeTracker.Clear();
                failures.Add(
                    $"{issue.OrderCode}: {exception.Message}");
            }
        }

        return new DevelopmentPaymentRepairBatchResult(
            issues.Length,
            repaired,
            failures.Count,
            failures.Take(10).ToArray());
    }

    private static PaymentTransaction? FindPaidCharge(Order order) =>
        order.PaymentTransactions
            .Where(item =>
                item.Provider == VnPayProvider
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

    private static PaymentTransaction? FindRefundLedger(
        IEnumerable<PaymentTransaction> transactions,
        long returnRequestId)
    {
        var key = $"{RefundIdempotencyPrefix}{returnRequestId}";
        return transactions.SingleOrDefault(item =>
            item.IdempotencyKey == key);
    }

    private static decimal CalculateRefundedAmount(Order order) =>
        order.PaymentTransactions
            .Where(item =>
                string.Equals(
                    item.Method,
                    RefundMethod,
                    StringComparison.OrdinalIgnoreCase)
                && item.Status == PaymentStatus.Refunded)
            .Sum(item => item.Amount);

    private static decimal CalculateTotalRefundedAmount(
        Order order)
    {
        var ledgerTransactions = order.PaymentTransactions
            .Where(item =>
                string.Equals(
                    item.Method,
                    RefundMethod,
                    StringComparison.OrdinalIgnoreCase)
                && item.Status == PaymentStatus.Refunded)
            .ToArray();

        var ledgerReturnIds = ledgerTransactions
            .Select(item => ParseReturnRequestId(item.IdempotencyKey))
            .Where(item => item.HasValue)
            .Select(item => item!.Value)
            .ToHashSet();

        var ledgerTotal = ledgerTransactions.Sum(item => item.Amount);
        var legacyTotal = order.ReturnRequests
            .Where(request =>
                request.Status is (
                    ReturnRequestStatus.Refunded
                    or ReturnRequestStatus.Closed)
                && !ledgerReturnIds.Contains(request.Id))
            .SelectMany(request => request.Items)
            .Sum(item => item.RefundAmount);

        return ledgerTotal + legacyTotal;
    }

    private static PaymentStatus CalculateExpectedPaymentStatus(
        Order order)
    {
        var paidCharge = FindPaidCharge(order);
        if (paidCharge is null)
        {
            return order.PaymentStatus;
        }

        var refunded = CalculateTotalRefundedAmount(order);

        if (refunded <= 0)
        {
            return PaymentStatus.Paid;
        }

        return refunded >= Math.Min(
                paidCharge.Amount,
                order.GrandTotal)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
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
            idempotencyKey[RefundIdempotencyPrefix.Length..],
            out var result)
                ? result
                : null;
    }

    private DevelopmentPaymentRepairResult Disabled() =>
        DevelopmentPaymentRepairResult.Failure(
            "DEV_RECONCILIATION_DISABLED",
            "Công cụ đối soát chỉ hoạt động trong Development.");

    private static string NormalizeActor(string? actor)
    {
        var normalized = actor?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "DevelopmentReconciliation";
        }

        return normalized.Length <= 100
            ? normalized
            : normalized[..100];
    }
}
