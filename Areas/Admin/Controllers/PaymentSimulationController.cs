using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Areas.Admin.ViewModels.PaymentSimulation;
using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Payments.Reconciliation;
using WebApplication2.Services.Payments.Simulation;

namespace WebApplication2.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/PaymentSimulation")]
public sealed class PaymentSimulationController : Controller
{
    private const string VnPayProvider = "VNPAY";

    private readonly ApplicationDbContext _context;
    private readonly IDevelopmentPaymentSimulator _simulator;
    private readonly IDevelopmentPaymentReconciliationService _reconciliation;
    private readonly ILogger<PaymentSimulationController> _logger;

    public PaymentSimulationController(
        ApplicationDbContext context,
        IDevelopmentPaymentSimulator simulator,
        IDevelopmentPaymentReconciliationService reconciliation,
        ILogger<PaymentSimulationController> logger)
    {
        _context = context;
        _simulator = simulator;
        _reconciliation = reconciliation;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken)
    {
        if (!_simulator.IsEnabled)
        {
            return NotFound();
        }

        var pendingPayments = await _context.Orders
            .AsNoTracking()
            .Where(order =>
                order.OrderStatus == OrderStatus.PendingPayment
                && order.PaymentStatus == PaymentStatus.Pending
                && order.PaymentTransactions.Any(payment =>
                    payment.Provider == VnPayProvider
                    && payment.Status == PaymentStatus.Pending))
            .OrderBy(order => order.CreatedAt)
            .Take(100)
            .Select(order => new PendingVnPaySimulationItemViewModel
            {
                OrderId = order.Id,
                OrderCode = order.Code,
                CustomerName = order.CustomerName,
                Amount = order.GrandTotal,
                Currency = order.Currency,
                PaymentTransactionId = order.PaymentTransactions
                    .Where(payment =>
                        payment.Provider == VnPayProvider
                        && payment.Status == PaymentStatus.Pending)
                    .OrderByDescending(payment => payment.AttemptNumber)
                    .ThenByDescending(payment => payment.Id)
                    .Select(payment => payment.Id)
                    .First(),
                MerchantReference = order.PaymentTransactions
                    .Where(payment =>
                        payment.Provider == VnPayProvider
                        && payment.Status == PaymentStatus.Pending)
                    .OrderByDescending(payment => payment.AttemptNumber)
                    .ThenByDescending(payment => payment.Id)
                    .Select(payment => payment.MerchantReference)
                    .First(),
                CreatedAt = order.CreatedAt
            })
            .ToArrayAsync(cancellationToken);

        var refunds = await _context.ReturnRequests
            .AsNoTracking()
            .Where(request =>
                request.Status == ReturnRequestStatus.RefundPending
                || request.Status == ReturnRequestStatus.Refunded)
            .Where(request =>
                request.Order.PaymentTransactions.Any(payment =>
                    payment.Provider == VnPayProvider))
            .OrderBy(request => request.RequestedAt)
            .Take(100)
            .Select(request =>
                new PendingRefundSimulationItemViewModel
                {
                    ReturnRequestId = request.Id,
                    ReturnCode = request.Code,
                    OrderId = request.OrderId,
                    OrderCode = request.Order.Code,
                    CustomerName = request.Order.CustomerName,
                    Status = request.Status,
                    RefundAmount = request.Items.Sum(
                        item => item.RefundAmount),
                    RequestedAt = request.RequestedAt
                })
            .ToArrayAsync(cancellationToken);

        return View(new PaymentSimulationPageViewModel
        {
            PendingPayments = pendingPayments,
            Refunds = refunds,
            Issues = await _reconciliation.ScanAsync(cancellationToken)
        });
    }

    [HttpPost("reconcile-safe")]
    public async Task<IActionResult> ReconcileSafe(
        CancellationToken cancellationToken)
    {
        if (!_reconciliation.IsEnabled)
        {
            return NotFound();
        }

        try
        {
            var result = await _reconciliation.RepairSafeIssuesAsync(
                ResolveActor(),
                cancellationToken);

            TempData["SuccessMessage"] =
                $"Đã rà soát {result.ScannedCount} sai lệch và sửa "
                + $"{result.RepairedCount} trường hợp.";

            if (result.FailedCount > 0)
            {
                TempData["ErrorMessage"] =
                    $"{result.FailedCount} trường hợp chưa thể tự sửa. "
                    + string.Join(" | ", result.Failures.Take(3));
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Development payment reconciliation failed.");

            TempData["ErrorMessage"] =
                "Không thể hoàn tất đối soát tự động.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("orders/{orderId:int}/repair-paid-state")]
    public Task<IActionResult> RepairPaidState(
        int orderId,
        CancellationToken cancellationToken) =>
        ExecuteRepairAsync(
            () => _reconciliation.RepairPaidOrderAsync(
                orderId,
                ResolveActor(),
                cancellationToken),
            "sửa trạng thái đơn đã thanh toán");

    [HttpPost("orders/{orderId:int}/repair-payment-total")]
    public Task<IActionResult> RepairPaymentTotal(
        int orderId,
        CancellationToken cancellationToken) =>
        ExecuteRepairAsync(
            () => _reconciliation.RepairOrderPaymentStatusAsync(
                orderId,
                ResolveActor(),
                cancellationToken),
            "đối chiếu tổng tiền hoàn");

    [HttpPost("returns/{returnRequestId:long}/repair-state")]
    public Task<IActionResult> RepairRefundState(
        long returnRequestId,
        CancellationToken cancellationToken) =>
        ExecuteRepairAsync(
            () => _reconciliation.RepairRefundStateAsync(
                returnRequestId,
                ResolveActor(),
                cancellationToken),
            "sửa trạng thái hoàn tiền");

    [HttpPost("returns/{returnRequestId:long}/backfill-ledger")]
    public Task<IActionResult> BackfillRefundLedger(
        long returnRequestId,
        CancellationToken cancellationToken) =>
        ExecuteRepairAsync(
            () => _reconciliation.BackfillLegacyRefundLedgerAsync(
                returnRequestId,
                ResolveActor(),
                cancellationToken),
            "tạo bút toán hoàn tiền lịch sử");

    [HttpPost("orders/{orderId:int}/confirm-payment")]
    public async Task<IActionResult> ConfirmPayment(
        int orderId,
        CancellationToken cancellationToken)
    {
        return await ExecuteAsync(
            () => _simulator.ConfirmVnPayPaymentAsync(
                orderId,
                ResolveActor(),
                cancellationToken),
            "xác nhận thanh toán",
            cancellationToken);
    }

    [HttpPost("returns/{returnRequestId:long}/confirm-refund")]
    public async Task<IActionResult> ConfirmRefund(
        long returnRequestId,
        CancellationToken cancellationToken)
    {
        return await ExecuteAsync(
            () => _simulator.ConfirmVnPayRefundAsync(
                returnRequestId,
                ResolveActor(),
                cancellationToken),
            "xác nhận hoàn tiền",
            cancellationToken);
    }

    [HttpPost("returns/{returnRequestId:long}/close")]
    public async Task<IActionResult> CloseRefund(
        long returnRequestId,
        CancellationToken cancellationToken)
    {
        return await ExecuteAsync(
            () => _simulator.CloseRefundAsync(
                returnRequestId,
                ResolveActor(),
                cancellationToken),
            "đóng yêu cầu hoàn trả",
            cancellationToken);
    }

    private async Task<IActionResult> ExecuteRepairAsync(
        Func<Task<DevelopmentPaymentRepairResult>> operation,
        string operationName)
    {
        if (!_reconciliation.IsEnabled)
        {
            return NotFound();
        }

        try
        {
            var result = await operation();
            TempData[result.Success
                ? "SuccessMessage"
                : "ErrorMessage"] = result.Message;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            _logger.LogWarning(
                exception,
                "Concurrent reconciliation during {Operation}.",
                operationName);

            TempData["ErrorMessage"] =
                "Dữ liệu vừa thay đổi. Hãy tải lại trang và thử lại.";
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Reconciliation failed during {Operation}.",
                operationName);

            TempData["ErrorMessage"] =
                $"Không thể {operationName}. Kiểm tra log ứng dụng.";
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task<IActionResult> ExecuteAsync(
        Func<Task<DevelopmentSimulationResult>> operation,
        string operationName,
        CancellationToken cancellationToken)
    {
        if (!_simulator.IsEnabled)
        {
            return NotFound();
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await operation();

            TempData[result.Success
                ? "SuccessMessage"
                : "ErrorMessage"] = result.Message;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            _logger.LogWarning(
                exception,
                "Concurrent development payment simulation during {Operation}.",
                operationName);

            TempData["ErrorMessage"] =
                "Dữ liệu vừa được thay đổi. Hãy tải lại trang và thử lại.";
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Development payment simulation failed during {Operation}.",
                operationName);

            TempData["ErrorMessage"] =
                $"Không thể {operationName}. Kiểm tra log ứng dụng để biết chi tiết.";
        }

        return RedirectToAction(nameof(Index));
    }

    private string ResolveActor() =>
        string.IsNullOrWhiteSpace(User.Identity?.Name)
            ? "DevelopmentSimulator"
            : User.Identity.Name;
}
