using Microsoft.AspNetCore.Mvc;
using WebApplication2.Areas.Admin.ViewModels.Orders;
using WebApplication2.Services.Commerce.Cancellations;
using WebApplication2.Services.Commerce.Inventory;

namespace WebApplication2.Areas.Admin.Controllers;

[Area("Admin")]
[Route("Admin/Orders/{orderId:int}/cancellations")]
public sealed class OrderCancellationsController : Controller
{
    private readonly IOrderCancellationService _cancellationService;
    private readonly ILogger<OrderCancellationsController> _logger;

    public OrderCancellationsController(
        IOrderCancellationService cancellationService,
        ILogger<OrderCancellationsController> logger)
    {
        _cancellationService = cancellationService;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Summary(
        int orderId,
        CancellationToken cancellationToken)
    {
        try
        {
            var summary = await _cancellationService.GetSummaryAsync(
                orderId,
                cancellationToken);
            return Json(new { success = true, data = summary });
        }
        catch (OrderCancellationException exception)
        {
            LogFlowException(exception, orderId, null);
            return NotFound(new
            {
                success = false,
                errorCode = exception.ErrorCode,
                message = exception.Message,
                flow = exception.FlowContext.FlowName,
                stage = exception.FlowContext.Stage.ToString(),
                correlationId = exception.FlowContext.CorrelationId
            });
        }
    }

    [HttpPost("")]
    public async Task<IActionResult> Create(
        int orderId,
        [FromBody] CreateOrderCancellationRequestModel request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new
            {
                success = false,
                errorCode = "INVALID_CANCELLATION_REQUEST",
                message = "Vui lòng chọn sản phẩm, số lượng và nhập đầy đủ lý do hủy."
            });
        }

        if (!TryDecodeRowVersion(request.OrderRowVersion, out var orderRowVersion))
        {
            return BadRequest(new
            {
                success = false,
                errorCode = "INVALID_ROW_VERSION",
                message = "RowVersion của đơn hàng không hợp lệ. Vui lòng tải lại."
            });
        }

        try
        {
            var summary = await _cancellationService.RequestAsync(
                new CreateOrderCancellationCommand(
                    orderId,
                    orderRowVersion,
                    request.ReasonCode,
                    request.ReasonText,
                    ResolveActor(),
                    request.IdempotencyKey,
                    request.Lines
                        .Select(item => new CancellationRequestLineCommand(
                            item.OrderItemId,
                            item.Quantity))
                        .ToArray()),
                cancellationToken);

            return Json(new
            {
                success = true,
                message = "Đã tạo yêu cầu hủy. Tồn kho chưa thay đổi cho đến khi yêu cầu được duyệt.",
                data = summary
            });
        }
        catch (OrderCancellationConcurrencyException exception)
        {
            LogFlowException(exception, orderId, null);
            return FlowConflictResponse(exception);
        }
        catch (OrderCancellationException exception)
        {
            LogFlowException(exception, orderId, null);
            return FlowRejectedResponse(exception);
        }
    }

    [HttpPost("{requestId:long}/review")]
    public async Task<IActionResult> Review(
        int orderId,
        long requestId,
        [FromBody] ReviewOrderCancellationRequestModel request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new
            {
                success = false,
                errorCode = "INVALID_CANCELLATION_REVIEW",
                message = "Dữ liệu duyệt yêu cầu hủy không hợp lệ."
            });
        }

        if (!TryDecodeRowVersion(request.OrderRowVersion, out var orderRowVersion)
            || !TryDecodeRowVersion(
                request.CancellationRowVersion,
                out var cancellationRowVersion))
        {
            return BadRequest(new
            {
                success = false,
                errorCode = "INVALID_ROW_VERSION",
                message = "RowVersion không hợp lệ. Vui lòng tải lại dữ liệu."
            });
        }

        try
        {
            var summary = await _cancellationService.ReviewAsync(
                new ReviewOrderCancellationCommand(
                    orderId,
                    requestId,
                    orderRowVersion,
                    cancellationRowVersion,
                    request.Approve,
                    ResolveActor(),
                    request.ReviewNote),
                cancellationToken);

            return Json(new
            {
                success = true,
                message = request.Approve
                    ? "Đã duyệt yêu cầu hủy và hoàn kho đúng một lần."
                    : "Đã từ chối yêu cầu hủy.",
                data = summary
            });
        }
        catch (OrderCancellationConcurrencyException exception)
        {
            LogFlowException(exception, orderId, requestId);
            return FlowConflictResponse(exception);
        }
        catch (OrderCancellationException exception)
        {
            LogFlowException(exception, orderId, requestId);
            return FlowRejectedResponse(exception);
        }
        catch (InventoryConflictException exception)
        {
            _logger.LogWarning(
                exception,
                "Inventory compensation conflict for cancellation request {CancellationRequestId}.",
                requestId);
            return ConflictResponse(
                "INVENTORY_COMPENSATION_CONFLICT",
                exception.Message);
        }
        catch (InventoryValidationException exception)
        {
            return UnprocessableEntity(new
            {
                success = false,
                errorCode = "INVENTORY_COMPENSATION_INVALID",
                message = exception.Message
            });
        }
    }

    private IActionResult FlowConflictResponse(OrderCancellationException exception)
    {
        return Conflict(new
        {
            success = false,
            errorCode = exception.ErrorCode,
            message = exception.Message,
            flow = exception.FlowContext.FlowName,
            stage = exception.FlowContext.Stage.ToString(),
            aggregateType = exception.FlowContext.AggregateType,
            aggregateId = exception.FlowContext.AggregateId,
            correlationId = exception.FlowContext.CorrelationId
        });
    }

    private IActionResult FlowRejectedResponse(OrderCancellationException exception)
    {
        return UnprocessableEntity(new
        {
            success = false,
            errorCode = exception.ErrorCode,
            message = exception.Message,
            flow = exception.FlowContext.FlowName,
            stage = exception.FlowContext.Stage.ToString(),
            aggregateType = exception.FlowContext.AggregateType,
            aggregateId = exception.FlowContext.AggregateId,
            correlationId = exception.FlowContext.CorrelationId
        });
    }

    private void LogFlowException(
        OrderCancellationException exception,
        int orderId,
        long? requestId)
    {
        _logger.LogWarning(
            exception,
            "Cancellation flow failed. ErrorCode={ErrorCode}; Flow={Flow}; Stage={Stage}; OrderId={OrderId}; RequestId={RequestId}; CorrelationId={CorrelationId}",
            exception.ErrorCode,
            exception.FlowContext.FlowName,
            exception.FlowContext.Stage,
            orderId,
            requestId,
            exception.FlowContext.CorrelationId);
    }

    private IActionResult ConflictResponse(string errorCode, string message)
    {
        return Conflict(new
        {
            success = false,
            errorCode,
            message
        });
    }

    private static bool TryDecodeRowVersion(
        string? value,
        out byte[] rowVersion)
    {
        rowVersion = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            rowVersion = Convert.FromBase64String(value);
            return rowVersion.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private string ResolveActor()
    {
        var name = User.Identity?.Name;
        return string.IsNullOrWhiteSpace(name) ? "Admin" : name;
    }
}
