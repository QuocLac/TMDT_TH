namespace WebApplication2.ViewModels.Storefront.Cart;

public sealed class CartPageViewModel
{
    public MockCustomerViewModel Customer { get; init; } = new();

    public IReadOnlyList<CartLineViewModel> Items { get; init; } = [];

    public int TotalLineCount { get; init; }

    public int TotalQuantity { get; init; }

    public int SelectedLineCount { get; init; }

    public int SelectedQuantity { get; init; }

    public decimal SelectedSubtotal { get; init; }

    public int UnavailableLineCount { get; init; }

    public bool AllAvailableSelected { get; init; }

    public bool HasSelectedItems => SelectedLineCount > 0;

    public bool IsEmpty => TotalLineCount == 0;
}

public sealed class CartLineViewModel
{
    public int ProductId { get; init; }

    public int VariantId { get; init; }

    public string ProductName { get; init; } = string.Empty;

    public string ProductSlug { get; init; } = string.Empty;

    public string Sku { get; init; } = string.Empty;

    public string ImageUrl { get; init; } = "/images/no-image.png";

    public string VariantDescription { get; init; } = string.Empty;

    public decimal OriginalPrice { get; init; }

    public decimal EffectivePrice { get; init; }

    public decimal LineTotal { get; init; }

    public int Quantity { get; init; }

    public int MaxQuantity { get; init; }

    public int StockQuantity { get; init; }

    public bool IsSelected { get; init; }

    public bool CanSelect { get; init; }

    public bool IsAvailable { get; init; }

    public bool IsOnSale => EffectivePrice > 0 && OriginalPrice > EffectivePrice;

    public bool PriceChanged { get; init; }

    public string? IssueCode { get; init; }

    public string? IssueMessage { get; init; }
}

public sealed class MockCustomerViewModel
{
    public string FullName { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string Phone { get; init; } = string.Empty;

    public string AddressLine { get; init; } = string.Empty;

    public string Ward { get; init; } = string.Empty;

    public string District { get; init; } = string.Empty;

    public string City { get; init; } = string.Empty;

    public string DisplayAddress => string.Join(", ", new[]
    {
        AddressLine,
        Ward,
        District,
        City
    }.Where(value => !string.IsNullOrWhiteSpace(value)));
}

public sealed class ProductOptionPickerViewModel
{
    public int ProductId { get; init; }

    public string ProductName { get; init; } = string.Empty;

    public string ProductSlug { get; init; } = string.Empty;

    public string ImageUrl { get; init; } = "/images/no-image.png";

    public decimal MinimumPrice { get; init; }

    public int AvailableVariantCount { get; init; }

    public IReadOnlyList<VariantAttributeGroupViewModel> AttributeGroups { get; init; } = [];

    public IReadOnlyList<QuickVariantViewModel> Variants { get; init; } = [];
}

public sealed class VariantAttributeGroupViewModel
{
    public string Key { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public IReadOnlyList<string> Values { get; init; } = [];
}

public sealed class QuickVariantViewModel
{
    public int Id { get; init; }

    public string Sku { get; init; } = string.Empty;

    public string ImageUrl { get; init; } = "/images/no-image.png";

    public decimal OriginalPrice { get; init; }

    public decimal EffectivePrice { get; init; }

    public int StockQuantity { get; init; }

    public bool IsAvailable => StockQuantity > 0;

    public IReadOnlyDictionary<string, string> Attributes { get; init; }
        = new Dictionary<string, string>();
}

public sealed class CartHeaderSnapshot
{
    public int TotalQuantity { get; init; }

    public int LineCount { get; init; }
}

public sealed class CartOperationResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public string? ErrorCode { get; init; }

    public string? RedirectUrl { get; init; }

    public CartPageViewModel? Cart { get; init; }

    public static CartOperationResult Failure(
        string message,
        string errorCode,
        CartPageViewModel? cart = null)
    {
        return new CartOperationResult
        {
            Success = false,
            Message = message,
            ErrorCode = errorCode,
            Cart = cart
        };
    }

    public static CartOperationResult Ok(
        string message,
        CartPageViewModel cart,
        string? redirectUrl = null)
    {
        return new CartOperationResult
        {
            Success = true,
            Message = message,
            Cart = cart,
            RedirectUrl = redirectUrl
        };
    }
}

public sealed class AddCartItemRequest
{
    public int ProductId { get; set; }

    public int VariantId { get; set; }

    public int Quantity { get; set; } = 1;

    public bool BuyNow { get; set; }
}

public sealed class UpdateCartQuantityRequest
{
    public int VariantId { get; set; }

    public int Quantity { get; set; }
}

public sealed class SetCartSelectionRequest
{
    public int VariantId { get; set; }

    public bool IsSelected { get; set; }
}

public sealed class SetAllCartSelectionRequest
{
    public bool IsSelected { get; set; }
}

public sealed class RemoveCartItemRequest
{
    public int VariantId { get; set; }
}

public sealed class UpdateMockCustomerRequest
{
    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public string AddressLine { get; set; } = string.Empty;

    public string Ward { get; set; } = string.Empty;

    public string District { get; set; } = string.Empty;

    public string City { get; set; } = string.Empty;
}
