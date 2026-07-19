using WebApplication2.Services.Catalog;
using WebApplication2.ViewModels.Storefront.Cart;

namespace WebApplication2.Services.Cart;

public sealed class DynamicProductOptionCartService : ISessionCartService
{
    private readonly SessionCartService _inner;
    private readonly IProductOptionReadService _productOptions;

    public DynamicProductOptionCartService(
        SessionCartService inner,
        IProductOptionReadService productOptions)
    {
        _inner = inner;
        _productOptions = productOptions;
    }

    public CartHeaderSnapshot GetHeaderSnapshot() =>
        _inner.GetHeaderSnapshot();

    public MockCustomerViewModel GetMockCustomer() =>
        _inner.GetMockCustomer();

    public long GetCartVersion() =>
        _inner.GetCartVersion();

    public string GetOrCreateCheckoutClientRequestId(long cartVersion) =>
        _inner.GetOrCreateCheckoutClientRequestId(cartVersion);

    public void CompleteCheckout(
        IReadOnlyCollection<int> purchasedVariantIds,
        string clientRequestId) =>
        _inner.CompleteCheckout(purchasedVariantIds, clientRequestId);

    public void ResetCheckoutClientRequestId(string? clientRequestId = null) =>
        _inner.ResetCheckoutClientRequestId(clientRequestId);

    public async Task<CartPageViewModel> GetCartAsync(
        CancellationToken cancellationToken)
    {
        var cart = await _inner.GetCartAsync(cancellationToken);
        return await EnrichCartAsync(cart, cancellationToken);
    }

    public async Task<ProductOptionPickerViewModel?> GetProductOptionsAsync(
        int productId,
        CancellationToken cancellationToken)
    {
        var snapshot = await _productOptions.GetProductPickerAsync(
            productId,
            cancellationToken);

        if (snapshot is null)
        {
            return await _inner.GetProductOptionsAsync(
                productId,
                cancellationToken);
        }

        return new ProductOptionPickerViewModel
        {
            ProductId = snapshot.ProductId,
            ProductName = snapshot.ProductName,
            ProductSlug = snapshot.ProductSlug,
            ImageUrl = snapshot.ImageUrl,
            MinimumPrice = snapshot.MinimumPrice,
            AvailableVariantCount = snapshot.AvailableItemCount,
            AttributeGroups = snapshot.Groups
                .Select(group => new VariantAttributeGroupViewModel
                {
                    Key = group.Code,
                    Label = group.Name,
                    Values = group.Values
                })
                .ToArray(),
            Variants = snapshot.Items
                .Select(item => new QuickVariantViewModel
                {
                    Id = item.VariantId,
                    Sku = item.Sku,
                    ImageUrl = item.ImageUrl,
                    OriginalPrice = item.OriginalPrice,
                    EffectivePrice = item.EffectivePrice,
                    StockQuantity = item.StockQuantity,
                    Attributes = new Dictionary<string, string>(
                        item.Selections,
                        StringComparer.OrdinalIgnoreCase)
                })
                .ToArray()
        };
    }

    public async Task<CartOperationResult> AddAsync(
        AddCartItemRequest request,
        CancellationToken cancellationToken) =>
        await NormalizeResultAsync(
            await _inner.AddAsync(request, cancellationToken),
            cancellationToken);

    public async Task<CartOperationResult> UpdateQuantityAsync(
        UpdateCartQuantityRequest request,
        CancellationToken cancellationToken) =>
        await NormalizeResultAsync(
            await _inner.UpdateQuantityAsync(request, cancellationToken),
            cancellationToken);

    public async Task<CartOperationResult> SetSelectionAsync(
        SetCartSelectionRequest request,
        CancellationToken cancellationToken) =>
        await NormalizeResultAsync(
            await _inner.SetSelectionAsync(request, cancellationToken),
            cancellationToken);

    public async Task<CartOperationResult> SetAllSelectionAsync(
        SetAllCartSelectionRequest request,
        CancellationToken cancellationToken) =>
        await NormalizeResultAsync(
            await _inner.SetAllSelectionAsync(request, cancellationToken),
            cancellationToken);

    public async Task<CartOperationResult> RemoveAsync(
        RemoveCartItemRequest request,
        CancellationToken cancellationToken) =>
        await NormalizeResultAsync(
            await _inner.RemoveAsync(request, cancellationToken),
            cancellationToken);

    public async Task<CartOperationResult> UpdateMockCustomerAsync(
        UpdateMockCustomerRequest request,
        CancellationToken cancellationToken) =>
        await NormalizeResultAsync(
            await _inner.UpdateMockCustomerAsync(request, cancellationToken),
            cancellationToken);

    public async Task<CartOperationResult> PrepareCheckoutAsync(
        CancellationToken cancellationToken) =>
        await NormalizeResultAsync(
            await _inner.PrepareCheckoutAsync(cancellationToken),
            cancellationToken);

    private async Task<CartOperationResult> NormalizeResultAsync(
        CartOperationResult result,
        CancellationToken cancellationToken)
    {
        var cart = result.Cart is null
            ? null
            : await EnrichCartAsync(result.Cart, cancellationToken);

        return new CartOperationResult
        {
            Success = result.Success,
            Message = Commercialize(result.Message),
            ErrorCode = result.ErrorCode,
            RedirectUrl = result.RedirectUrl,
            Cart = cart
        };
    }

    private async Task<CartPageViewModel> EnrichCartAsync(
        CartPageViewModel cart,
        CancellationToken cancellationToken)
    {
        if (cart.Items.Count == 0)
        {
            return cart;
        }

        var labels = await _productOptions.GetVariantLabelsAsync(
            cart.Items.Select(item => item.VariantId),
            cancellationToken);

        var items = cart.Items
            .Select(item => new CartLineViewModel
            {
                ProductId = item.ProductId,
                VariantId = item.VariantId,
                ProductName = item.ProductName,
                ProductSlug = item.ProductSlug,
                Sku = item.Sku,
                ImageUrl = item.ImageUrl,
                VariantDescription = labels.TryGetValue(
                    item.VariantId,
                    out var label)
                    && !string.IsNullOrWhiteSpace(label)
                        ? label
                        : Commercialize(item.VariantDescription),
                OriginalPrice = item.OriginalPrice,
                EffectivePrice = item.EffectivePrice,
                LineTotal = item.LineTotal,
                Quantity = item.Quantity,
                MaxQuantity = item.MaxQuantity,
                StockQuantity = item.StockQuantity,
                IsSelected = item.IsSelected,
                CanSelect = item.CanSelect,
                IsAvailable = item.IsAvailable,
                PriceChanged = item.PriceChanged,
                IssueCode = item.IssueCode,
                IssueMessage = Commercialize(item.IssueMessage)
            })
            .ToArray();

        return new CartPageViewModel
        {
            Customer = cart.Customer,
            Items = items,
            TotalLineCount = cart.TotalLineCount,
            TotalQuantity = cart.TotalQuantity,
            SelectedLineCount = cart.SelectedLineCount,
            SelectedQuantity = cart.SelectedQuantity,
            SelectedSubtotal = cart.SelectedSubtotal,
            UnavailableLineCount = cart.UnavailableLineCount,
            AllAvailableSelected = cart.AllAvailableSelected
        };
    }

    private static string Commercialize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value
            .Replace(
                "Dữ liệu biến thể",
                "Lựa chọn mua",
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "Biến thể",
                "Lựa chọn mua",
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "biến thể",
                "lựa chọn mua",
                StringComparison.OrdinalIgnoreCase);
    }
}
