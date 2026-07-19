using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;
using WebApplication2.Services.Catalog;

namespace WebApplication2.Services.Commerce.Orders;

public sealed class DynamicProductOptionOrderApplicationService
    : IOrderApplicationService
{
    private readonly OrderApplicationService _inner;
    private readonly ApplicationDbContext _context;
    private readonly IProductOptionReadService _productOptions;
    private readonly ILogger<DynamicProductOptionOrderApplicationService> _logger;

    public DynamicProductOptionOrderApplicationService(
        OrderApplicationService inner,
        ApplicationDbContext context,
        IProductOptionReadService productOptions,
        ILogger<DynamicProductOptionOrderApplicationService> logger)
    {
        _inner = inner;
        _context = context;
        _productOptions = productOptions;
        _logger = logger;
    }

    public Task<PlaceOrderResult?> FindByClientRequestIdAsync(
        string clientRequestId,
        int customerId,
        CancellationToken cancellationToken) =>
        _inner.FindByClientRequestIdAsync(
            clientRequestId,
            customerId,
            cancellationToken);

    public async Task<PlaceOrderResult> PlaceOrderAsync(
        PlaceOrderCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _inner.PlaceOrderAsync(
            command,
            cancellationToken);

        await TryApplyPurchaseOptionSnapshotsAsync(
            result.OrderId,
            cancellationToken);

        return result;
    }

    public Task<OrderReceipt?> GetReceiptAsync(
        Guid publicToken,
        int customerId,
        CancellationToken cancellationToken) =>
        _inner.GetReceiptAsync(
            publicToken,
            customerId,
            cancellationToken);

    private async Task TryApplyPurchaseOptionSnapshotsAsync(
        int orderId,
        CancellationToken cancellationToken)
    {
        try
        {
            var lines = await _context.OrderItems
                .Where(item => item.OrderId == orderId)
                .ToArrayAsync(cancellationToken);

            if (lines.Length == 0)
            {
                return;
            }

            var labels = await _productOptions.GetVariantLabelsAsync(
                lines.Select(item => item.ProductVariantId),
                cancellationToken);

            var changed = false;

            foreach (var line in lines)
            {
                if (!labels.TryGetValue(
                        line.ProductVariantId,
                        out var label)
                    || string.IsNullOrWhiteSpace(label)
                    || string.Equals(
                        line.VariantDescription,
                        label,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                line.VariantDescription = label;
                changed = true;
            }

            if (changed)
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception exception)
        {
            // Đơn đã được tạo thành công ở dịch vụ lõi. Không làm thất bại
            // checkout chỉ vì bước chuẩn hóa nhãn hiển thị gặp lỗi.
            _logger.LogWarning(
                exception,
                "Could not apply dynamic product option snapshots to order {OrderId}.",
                orderId);
        }
    }
}
