using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Commerce.Flows;
using WebApplication2.Services.Commerce.Returns;
using WebApplication2.Services.Identity;
using WebApplication2.ViewModels.Storefront.Returns;

namespace WebApplication2.Controllers;

[Authorize]
[Route("returns")]
public sealed class ReturnsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IReturnWorkflowService _workflow;
    private readonly IReturnRefundDestinationService _refundDestination;
    private readonly ILogger<ReturnsController> _logger;

    public ReturnsController(
        ApplicationDbContext context,
        IReturnWorkflowService workflow,
        IReturnRefundDestinationService refundDestination,
        ILogger<ReturnsController> logger)
    {
        _context = context;
        _workflow = workflow;
        _refundDestination = refundDestination;
        _logger = logger;
    }

    [HttpGet("request/{publicToken:guid}")]
    public async Task<IActionResult> RequestReturn(
        Guid publicToken,
        CancellationToken cancellationToken)
    {
        if (!await OwnsOrderAsync(publicToken, cancellationToken))
        {
            return NotFound();
        }

        var eligibility = await _workflow.GetEligibilityAsync(
            publicToken,
            cancellationToken);
        if (eligibility is null)
        {
            return NotFound();
        }

        return View("Request", BuildRequestPage(
            eligibility,
            new CreateReturnRequestInput
            {
                OrderPublicToken = publicToken,
                IdempotencyKey = Guid.NewGuid().ToString("N"),
                RefundBankBin = _refundDestination.Banks.First().Bin,
                Lines = eligibility.Items
                    .OrderBy(item => item.OrderItemId)
                    .Select(item => new CreateReturnLineInput
                    {
                        OrderItemId = item.OrderItemId,
                        Quantity = item.PurchasedQuantity
                    })
                    .ToList()
            },
            null));
    }

    [HttpPost("request")]
    public async Task<IActionResult> RequestReturn(
        [Bind(Prefix = "Form")] CreateReturnRequestInput input,
        CancellationToken cancellationToken)
    {
        if (!await OwnsOrderAsync(input.OrderPublicToken, cancellationToken))
        {
            return NotFound();
        }

        var eligibility = await _workflow.GetEligibilityAsync(
            input.OrderPublicToken,
            cancellationToken);
        if (eligibility is null)
        {
            return NotFound();
        }

        var canReturnWholeOrder = eligibility.IsEligible
            && eligibility.Items.Count > 0
            && eligibility.Items.All(item =>
                item.PurchasedQuantity > 0
                && item.CancelledQuantity == 0
                && item.ReservedReturnQuantity == 0
                && item.AvailableQuantity == item.PurchasedQuantity);

        if (!canReturnWholeOrder)
        {
            ModelState.AddModelError(
                string.Empty,
                eligibility.IsEligible
                    ? "Đơn hàng không còn nguyên vẹn để tạo yêu cầu hoàn trả mới."
                    : eligibility.Message);
        }

        IReadOnlyList<ReturnEvidenceCommand> evidence = [];
        ReturnRefundDestination? refundDestination = null;
        try
        {
            evidence = ParseUploadedImages(input.EvidenceUrls);
            refundDestination = _refundDestination.Validate(
                input.RefundBankBin,
                input.RefundAccountNumber,
                input.RefundAccountName);
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
        }

        var wholeOrderLines = eligibility.Items
            .OrderBy(item => item.OrderItemId)
            .Select(item => new ReturnRequestLineCommand(
                item.OrderItemId,
                item.PurchasedQuantity))
            .ToArray();

        if (!ModelState.IsValid)
        {
            return View("Request", BuildRequestPage(
                eligibility,
                input,
                FirstModelError()));
        }

        try
        {
            var request = await _workflow.CreateRequestAsync(
                new CreateReturnRequestCommand(
                    input.OrderPublicToken,
                    input.ReasonCode,
                    input.ReasonText,
                    User.Identity?.Name ?? eligibility.CustomerEmail,
                    input.IdempotencyKey,
                    wholeOrderLines,
                    evidence),
                cancellationToken);

            await _refundDestination.SaveAsync(
                request,
                refundDestination!,
                cancellationToken);

            TempData["SuccessMessage"] =
                "Yêu cầu hoàn trả đã được ghi nhận.";

            return RedirectToAction(
                nameof(Confirmation),
                new
                {
                    returnCode = request.Code,
                    publicToken = input.OrderPublicToken
                });
        }
        catch (CommerceFlowException exception)
        {
            LogFlow(exception);
            var message = FriendlyFlowMessage(exception);
            ModelState.AddModelError(string.Empty, message);
            return View("Request", BuildRequestPage(
                eligibility,
                input,
                message));
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(string.Empty, exception.Message);
            return View("Request", BuildRequestPage(
                eligibility,
                input,
                exception.Message));
        }
    }

    [HttpGet("{returnCode}/confirmation")]
    public async Task<IActionResult> Confirmation(
        string returnCode,
        Guid publicToken,
        CancellationToken cancellationToken)
    {
        var customerId = RequireCustomerId();
        var request = await _context.ReturnRequests
            .AsNoTracking()
            .Include(item => item.Order)
            .Include(item => item.Items)
                .ThenInclude(item => item.OrderItem)
            .Include(item => item.Shipments)
            .SingleOrDefaultAsync(
                item => item.Code == returnCode
                    && item.Order.PublicToken == publicToken
                    && item.Order.CustomerId == customerId,
                cancellationToken);

        if (request is null)
        {
            return NotFound();
        }

        var shipment = request.Shipments
            .Where(item => item.Direction == ShipmentDirection.Return)
            .OrderByDescending(item => item.Id)
            .FirstOrDefault();
        var destination = await _refundDestination.GetAsync(
            request.Id,
            cancellationToken);

        return View(new ReturnConfirmationViewModel
        {
            ReturnCode = request.Code,
            OrderCode = request.Order.Code,
            OrderPublicToken = request.Order.PublicToken,
            Status = request.Status,
            StatusText = ReturnStatusText(request.Status),
            ReasonCode = request.ReasonCode,
            ReasonText = request.ReasonText,
            RequestedAt = request.RequestedAt,
            ReturnWindowExpiresAt = request.ReturnWindowExpiresAt,
            TrackingCode = shipment?.TrackingCode,
            RefundBankName = destination?.BankName ?? "Chưa xác định",
            RefundAccountNumber = destination?.MaskedAccountNumber ?? "Chưa xác định",
            RefundAccountName = destination?.AccountName ?? "Chưa xác định",
            Progress = BuildProgress(request.Status),
            Items = request.Items
                .OrderBy(item => item.Id)
                .Select(item => new ReturnConfirmationLineViewModel
                {
                    ProductName = item.OrderItem.ProductName,
                    Sku = item.OrderItem.Sku,
                    VariantDescription = item.OrderItem.VariantDescription,
                    RequestedQuantity = item.RequestedQuantity,
                    ApprovedQuantity = item.ApprovedQuantity,
                    ReceivedQuantity = item.ReceivedQuantity,
                    RefundAmount = item.RefundAmount
                })
                .ToArray()
        });
    }

    private async Task<bool> OwnsOrderAsync(
        Guid publicToken,
        CancellationToken cancellationToken)
    {
        if (publicToken == Guid.Empty)
        {
            return false;
        }

        var customerId = RequireCustomerId();
        return await _context.Orders
            .AsNoTracking()
            .AnyAsync(
                item => item.PublicToken == publicToken
                    && item.CustomerId == customerId,
                cancellationToken);
    }

    private int RequireCustomerId() =>
        HttpContext.User.GetCustomerId()
        ?? throw new InvalidOperationException(
            "Authenticated account has no CustomerId claim.");

    private IReadOnlyList<ReturnEvidenceCommand> ParseUploadedImages(
        string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var urls = raw
            .Split(
                ['\r', '\n', ','],
                StringSplitOptions.RemoveEmptyEntries
                | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(9)
            .ToArray();

        if (urls.Length > 8)
        {
            throw new InvalidOperationException(
                "Mỗi yêu cầu được chọn tối đa 8 ảnh.");
        }

        return urls.Select(url =>
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || !string.Equals(
                    uri.Host,
                    Request.Host.Host,
                    StringComparison.OrdinalIgnoreCase)
                || !uri.AbsolutePath.StartsWith(
                    "/uploads/",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Ảnh minh chứng phải được chọn và tải lên trực tiếp từ thiết bị.");
            }

            return new ReturnEvidenceCommand(
                ReturnEvidenceType.Image,
                uri.ToString(),
                null);
        }).ToArray();
    }

    private ReturnRequestPageViewModel BuildRequestPage(
        ReturnEligibilitySnapshot eligibility,
        CreateReturnRequestInput form,
        string? error) => new()
    {
        Eligibility = eligibility,
        Form = form,
        Banks = _refundDestination.Banks,
        ErrorMessage = error
    };

    private string FirstModelError() => ModelState.Values
        .SelectMany(item => item.Errors)
        .Select(item => item.ErrorMessage)
        .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item))
        ?? "Yêu cầu hoàn trả chưa hợp lệ.";

    private void LogFlow(CommerceFlowException exception)
    {
        _logger.LogWarning(
            exception,
            "Return storefront flow failed. ErrorCode={ErrorCode}; Stage={Stage}; Aggregate={AggregateType}:{AggregateId}; CorrelationId={CorrelationId}",
            exception.ErrorCode,
            exception.FlowContext.Stage,
            exception.FlowContext.AggregateType,
            exception.FlowContext.AggregateId,
            exception.FlowContext.CorrelationId);
    }

    private static string FriendlyFlowMessage(
        CommerceFlowException exception) => exception.ErrorCode switch
    {
        "RETURN_EVIDENCE_REQUIRED" =>
            "Vui lòng chọn ít nhất một ảnh thể hiện tình trạng sản phẩm.",
        "RETURN_WINDOW_EXPIRED" =>
            "Đơn hàng đã hết thời hạn gửi yêu cầu hoàn trả.",
        "RETURN_NO_QUANTITY_AVAILABLE" =>
            "Đơn hàng không còn sản phẩm đủ điều kiện hoàn trả.",
        "RETURN_QUANTITY_EXCEEDED" =>
            "Số lượng hoàn trả không còn phù hợp. Vui lòng tải lại trang.",
        _ => "Không thể gửi yêu cầu lúc này. Vui lòng kiểm tra lại thông tin và thử lại."
    };

    private static string ReturnStatusText(ReturnRequestStatus status) =>
        status switch
        {
            ReturnRequestStatus.Requested => "Đã gửi yêu cầu",
            ReturnRequestStatus.UnderReview => "Đang xem xét",
            ReturnRequestStatus.Approved => "Đã chấp nhận",
            ReturnRequestStatus.Rejected => "Không được chấp nhận",
            ReturnRequestStatus.AwaitingReturnShipment => "Chờ tiếp nhận hàng hoàn",
            ReturnRequestStatus.AwaitingPickup => "Chờ bàn giao hàng hoàn",
            ReturnRequestStatus.ReturnInTransit => "Hàng hoàn đang được vận chuyển",
            ReturnRequestStatus.ReceivedAtWarehouse => "FastBuy đã nhận hàng hoàn",
            ReturnRequestStatus.Inspecting => "Đang kiểm tra sản phẩm",
            ReturnRequestStatus.RefundPending => "Đang hoàn tiền",
            ReturnRequestStatus.RejectedAfterInspection => "Không đủ điều kiện hoàn tiền",
            ReturnRequestStatus.Refunded => "Đã hoàn tiền",
            ReturnRequestStatus.Closed => "Đã hoàn tất",
            ReturnRequestStatus.Cancelled => "Đã hủy",
            _ => "Đang xử lý"
        };

    private static IReadOnlyList<ReturnProgressStepViewModel> BuildProgress(
        ReturnRequestStatus status)
    {
        var current = status switch
        {
            ReturnRequestStatus.Requested => 1,
            ReturnRequestStatus.UnderReview => 2,
            ReturnRequestStatus.Approved
                or ReturnRequestStatus.AwaitingReturnShipment
                or ReturnRequestStatus.AwaitingPickup => 3,
            ReturnRequestStatus.ReturnInTransit => 4,
            ReturnRequestStatus.ReceivedAtWarehouse
                or ReturnRequestStatus.Inspecting => 5,
            ReturnRequestStatus.RefundPending => 6,
            ReturnRequestStatus.Refunded
                or ReturnRequestStatus.Closed => 7,
            ReturnRequestStatus.Rejected
                or ReturnRequestStatus.RejectedAfterInspection
                or ReturnRequestStatus.Cancelled => 2,
            _ => 1
        };

        var steps = new[]
        {
            ("Đã gửi", "Yêu cầu đã được ghi nhận."),
            ("Xem xét", "FastBuy kiểm tra điều kiện hoàn trả."),
            ("Bàn giao", "Đóng gói và bàn giao toàn bộ đơn."),
            ("Vận chuyển", "Hàng đang được đưa về FastBuy."),
            ("Kiểm tra", "Sản phẩm được tiếp nhận và kiểm tra."),
            ("Hoàn tiền", "Khoản hoàn được chuyển về tài khoản đã đăng ký."),
            ("Hoàn tất", "Yêu cầu đã được xử lý xong.")
        };

        return steps.Select((step, index) =>
            new ReturnProgressStepViewModel
            {
                Position = index + 1,
                Title = step.Item1,
                Description = step.Item2,
                IsComplete = index + 1 <= current,
                IsCurrent = index + 1 == current
            }).ToArray();
    }
}
