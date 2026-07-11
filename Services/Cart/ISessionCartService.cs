using WebApplication2.ViewModels.Storefront.Cart;

namespace WebApplication2.Services.Cart;

public interface ISessionCartService
{
    CartHeaderSnapshot GetHeaderSnapshot();

    MockCustomerViewModel GetMockCustomer();

    Task<CartPageViewModel> GetCartAsync(CancellationToken cancellationToken);

    Task<ProductOptionPickerViewModel?> GetProductOptionsAsync(
        int productId,
        CancellationToken cancellationToken);

    Task<CartOperationResult> AddAsync(
        AddCartItemRequest request,
        CancellationToken cancellationToken);

    Task<CartOperationResult> UpdateQuantityAsync(
        UpdateCartQuantityRequest request,
        CancellationToken cancellationToken);

    Task<CartOperationResult> SetSelectionAsync(
        SetCartSelectionRequest request,
        CancellationToken cancellationToken);

    Task<CartOperationResult> SetAllSelectionAsync(
        SetAllCartSelectionRequest request,
        CancellationToken cancellationToken);

    Task<CartOperationResult> RemoveAsync(
        RemoveCartItemRequest request,
        CancellationToken cancellationToken);

    Task<CartOperationResult> UpdateMockCustomerAsync(
        UpdateMockCustomerRequest request,
        CancellationToken cancellationToken);

    Task<CartOperationResult> PrepareCheckoutAsync(
        CancellationToken cancellationToken);
}
