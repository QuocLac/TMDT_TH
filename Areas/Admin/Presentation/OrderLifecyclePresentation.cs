using WebApplication2.Areas.Admin.Controllers;
using WebApplication2.Areas.Admin.ViewModels.Orders;
using WebApplication2.Models.Enums;

namespace WebApplication2.Areas.Admin.Presentation;

public sealed record OrderLifecycleActionPresentation(
    string ActionName,
    string TargetValue,
    string Label,
    string Description);

public sealed record OrderLifecycleStepPresentation(
    string Key,
    int Position,
    string Title,
    string Description,
    string StateText,
    string Tone,
    bool IsComplete,
    bool IsCurrent,
    string ControlText,
    IReadOnlyList<OrderLifecycleActionPresentation> Actions,
    IReadOnlyList<OrderHistoryCategory> HistoryCategories);

public static class OrderLifecyclePresentation
{
    public static IReadOnlyList<OrderLifecycleStepPresentation> Build(
        OrderAdminDetailsViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var paymentGatePassed = model.PaymentStatus is
            PaymentStatus.Paid
            or PaymentStatus.CodPending
            or PaymentStatus.PartiallyRefunded
            or PaymentStatus.Refunded;

        var confirmed = model.OrderStatus is
            OrderStatus.Confirmed
            or OrderStatus.Processing
            or OrderStatus.Completed
            or OrderStatus.Closed;

        var processing = model.OrderStatus is
            OrderStatus.Processing
            or OrderStatus.Completed
            or OrderStatus.Closed;

        var preparing = model.FulfillmentStatus is
            FulfillmentStatus.Preparing
            or FulfillmentStatus.ReadyToShip
            or FulfillmentStatus.Shipped
            or FulfillmentStatus.Delivered
            or FulfillmentStatus.Returning
            or FulfillmentStatus.Returned;

        var readyToShip = model.FulfillmentStatus is
            FulfillmentStatus.ReadyToShip
            or FulfillmentStatus.Shipped
            or FulfillmentStatus.Delivered
            or FulfillmentStatus.Returning
            or FulfillmentStatus.Returned;

        var shipped = model.FulfillmentStatus is
            FulfillmentStatus.Shipped
            or FulfillmentStatus.Delivered
            or FulfillmentStatus.Returning
            or FulfillmentStatus.Returned;

        var delivered = model.FulfillmentStatus == FulfillmentStatus.Delivered;
        var completed = model.OrderStatus is OrderStatus.Completed or OrderStatus.Closed;

        var rawSteps = new[]
        {
            Create(
                "received",
                1,
                "Tiếp nhận",
                "Đơn được tạo và khóa dữ liệu bán hàng ban đầu.",
                AdminCommerceDisplay.OrderStatusText(model.OrderStatus),
                "info",
                isComplete: true,
                "Hệ thống tạo đơn và giữ tồn kho theo checkout.",
                [],
                [OrderHistoryCategory.Order, OrderHistoryCategory.Inventory]),

            Create(
                "payment",
                2,
                "Thanh toán",
                "Xác minh thanh toán trực tuyến hoặc điều kiện thu tiền khi giao.",
                AdminCommerceDisplay.PaymentStatusText(model.PaymentStatus),
                AdminCommerceDisplay.Tone(model.PaymentStatus),
                paymentGatePassed,
                model.PaymentStatus == PaymentStatus.CodPending
                    ? "COD chỉ chuyển thành đã thanh toán sau khi đơn vị vận chuyển xác nhận giao thành công."
                    : "Trạng thái thanh toán được cập nhật từ callback của cổng thanh toán hoặc nghiệp vụ COD.",
                [],
                [OrderHistoryCategory.Payment, OrderHistoryCategory.Integration]),

            Create(
                "confirmed",
                3,
                "Xác nhận đơn",
                "Admin kiểm tra thông tin khách, giá, thanh toán và khả năng đáp ứng.",
                AdminCommerceDisplay.OrderStatusText(model.OrderStatus),
                AdminCommerceDisplay.Tone(model.OrderStatus),
                confirmed,
                "Chỉ xác nhận khi trạng thái thanh toán cho phép tiếp tục xử lý.",
                BuildOrderAction(
                    model,
                    OrderStatus.Confirmed,
                    "Xác nhận đơn",
                    "Ghi nhận đơn hợp lệ và chuyển sang bước chuẩn bị."),
                [OrderHistoryCategory.Order]),

            Create(
                "preparing",
                4,
                "Chuẩn bị hàng",
                "Chuyển đơn sang xử lý và bắt đầu chuẩn bị hàng trong kho.",
                processing && preparing
                    ? "Đang chuẩn bị hàng"
                    : $"{AdminCommerceDisplay.OrderStatusText(model.OrderStatus)} · {AdminCommerceDisplay.FulfillmentStatusText(model.FulfillmentStatus)}",
                processing && preparing ? "info" : "neutral",
                processing && preparing,
                "Bước này có thể cần hai thao tác: chuyển đơn sang đang xử lý và mở công việc chuẩn bị hàng.",
                BuildPreparingActions(model),
                [OrderHistoryCategory.Order, OrderHistoryCategory.Fulfillment, OrderHistoryCategory.Inventory]),

            Create(
                "ready",
                5,
                "Sẵn sàng bàn giao",
                "Hàng đã được soạn, kiểm tra và sẵn sàng tạo hoặc bàn giao vận đơn.",
                AdminCommerceDisplay.FulfillmentStatusText(model.FulfillmentStatus),
                AdminCommerceDisplay.Tone(model.FulfillmentStatus),
                readyToShip,
                "Chỉ đánh dấu sẵn sàng khi số lượng thực tế và giữ tồn kho đã hợp lệ.",
                BuildFulfillmentAction(
                    model,
                    FulfillmentStatus.ReadyToShip,
                    "Đánh dấu sẵn sàng bàn giao",
                    "Xác nhận đóng gói hoàn tất và sẵn sàng giao cho đơn vị vận chuyển."),
                [OrderHistoryCategory.Fulfillment, OrderHistoryCategory.Inventory]),

            Create(
                "shipping",
                6,
                "Đang giao",
                "Vận đơn đã được đơn vị vận chuyển tiếp nhận và đang di chuyển.",
                AdminCommerceDisplay.FulfillmentStatusText(model.FulfillmentStatus),
                AdminCommerceDisplay.Tone(model.FulfillmentStatus),
                shipped,
                "Không cập nhật thủ công. Trạng thái này phải đến từ dữ liệu vận chuyển.",
                [],
                [OrderHistoryCategory.Fulfillment, OrderHistoryCategory.Integration]),

            Create(
                "delivered",
                7,
                "Giao thành công",
                "Đơn vị vận chuyển xác nhận hàng đã giao tới khách.",
                AdminCommerceDisplay.FulfillmentStatusText(model.FulfillmentStatus),
                AdminCommerceDisplay.Tone(model.FulfillmentStatus),
                delivered,
                model.PaymentStatus == PaymentStatus.CodPending
                    ? "Khi GHN xác nhận giao thành công, luồng COD phải cập nhật thanh toán trước khi hoàn tất đơn."
                    : "Không cập nhật thủ công. Dữ liệu giao thành công đến từ provider.",
                [],
                [OrderHistoryCategory.Fulfillment, OrderHistoryCategory.Payment, OrderHistoryCategory.Integration]),

            Create(
                "completed",
                8,
                "Hoàn tất",
                "Đơn hoàn tất khi giao hàng và thanh toán đều đạt điều kiện nghiệp vụ.",
                AdminCommerceDisplay.OrderStatusText(model.OrderStatus),
                AdminCommerceDisplay.Tone(model.OrderStatus),
                completed,
                "Hệ thống chỉ hoàn tất đơn sau khi các điều kiện giao hàng, thanh toán và tồn kho đã đồng bộ.",
                [],
                [OrderHistoryCategory.Order, OrderHistoryCategory.Payment, OrderHistoryCategory.Fulfillment])
        };

        var currentIndex = Array.FindIndex(rawSteps, step => !step.IsComplete);
        if (currentIndex < 0)
        {
            currentIndex = rawSteps.Length - 1;
        }

        return rawSteps
            .Select((step, index) => step with { IsCurrent = index == currentIndex })
            .ToArray();
    }

    private static OrderLifecycleStepPresentation Create(
        string key,
        int position,
        string title,
        string description,
        string stateText,
        string tone,
        bool isComplete,
        string controlText,
        IReadOnlyList<OrderLifecycleActionPresentation> actions,
        IReadOnlyList<OrderHistoryCategory> historyCategories) =>
        new(
            key,
            position,
            title,
            description,
            stateText,
            tone,
            isComplete,
            false,
            controlText,
            actions,
            historyCategories);

    private static IReadOnlyList<OrderLifecycleActionPresentation> BuildOrderAction(
        OrderAdminDetailsViewModel model,
        OrderStatus target,
        string label,
        string description) =>
        model.AllowedOrderTransitions.Contains(target)
            ? [new(nameof(OrdersController.ChangeOrderStatus), target.ToString(), label, description)]
            : [];

    private static IReadOnlyList<OrderLifecycleActionPresentation> BuildFulfillmentAction(
        OrderAdminDetailsViewModel model,
        FulfillmentStatus target,
        string label,
        string description) =>
        model.AllowedFulfillmentTransitions.Contains(target)
            ? [new(nameof(OrdersController.ChangeFulfillmentStatus), target.ToString(), label, description)]
            : [];

    private static IReadOnlyList<OrderLifecycleActionPresentation> BuildPreparingActions(
        OrderAdminDetailsViewModel model)
    {
        var actions = new List<OrderLifecycleActionPresentation>();

        if (model.AllowedOrderTransitions.Contains(OrderStatus.Processing))
        {
            actions.Add(new(
                nameof(OrdersController.ChangeOrderStatus),
                OrderStatus.Processing.ToString(),
                "Chuyển đơn sang đang xử lý",
                "Mở giai đoạn xử lý thương mại của đơn."));
        }

        if (model.AllowedFulfillmentTransitions.Contains(FulfillmentStatus.Preparing))
        {
            actions.Add(new(
                nameof(OrdersController.ChangeFulfillmentStatus),
                FulfillmentStatus.Preparing.ToString(),
                "Bắt đầu chuẩn bị hàng",
                "Ghi nhận kho bắt đầu soạn và kiểm tra hàng."));
        }

        return actions;
    }
}
