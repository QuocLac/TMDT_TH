using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Areas.Admin.ViewModels.PaymentSimulation;
using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Payments.Simulation;

namespace WebApplication2.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/PaymentSimulation")]
public sealed class PaymentSimulationController : Controller
{
    private const string VnPayProvider = "VNPAY";

    private readonly ApplicationDbContext _context;
    private readonly IDevelopmentPaymentSimulator _simulator;
    private readonly ILogger<PaymentSimulationController> _logger;

    public PaymentSimulationController(
        ApplicationDbContext context,
        IDevelopmentPaymentSimulator simulator,
        ILogger<PaymentSimulationController> logger)
    {
        _context = context;
        _simulator = simulator;
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
            Refunds = refunds
        });
    }

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
