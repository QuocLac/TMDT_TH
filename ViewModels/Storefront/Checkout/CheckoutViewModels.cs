using System.ComponentModel.DataAnnotations;
using WebApplication2.ViewModels.Storefront.Cart;

namespace WebApplication2.ViewModels.Storefront.Checkout;

public sealed class CheckoutPageViewModel
{
    public MockCustomerViewModel Customer { get; init; } = new();

    public IReadOnlyList<CheckoutLineViewModel> Items { get; init; } = [];

    public decimal Subtotal { get; init; }

    public IReadOnlyList<CheckoutProvinceOption> Provinces { get; init; } = [];

    public string? AddressLookupError { get; init; }

    public static CheckoutPageViewModel FromCart(
        CartPageViewModel cart,
        IReadOnlyList<CheckoutProvinceOption> provinces,
        string? addressLookupError = null)
    {
        var items = cart.Items
            .Where(item => item.IsSelected && item.CanSelect)
            .Select(item => new CheckoutLineViewModel
            {
                ProductName = item.ProductName,
                VariantDescription = item.VariantDescription,
                Sku = item.Sku,
                Quantity = item.Quantity,
                UnitPrice = item.EffectivePrice,
                LineTotal = item.LineTotal,
                ImageUrl = item.ImageUrl
            })
            .ToArray();

        return new CheckoutPageViewModel
        {
            Customer = cart.Customer,
            Items = items,
            Subtotal = items.Sum(item => item.LineTotal),
            Provinces = provinces,
            AddressLookupError = addressLookupError
        };
    }
}

public sealed class CheckoutLineViewModel
{
    public string ProductName { get; init; } = string.Empty;

    public string VariantDescription { get; init; } = string.Empty;

    public string Sku { get; init; } = string.Empty;

    public string ImageUrl { get; init; } = "/images/no-image.png";

    public int Quantity { get; init; }

    public decimal UnitPrice { get; init; }

    public decimal LineTotal { get; init; }
}

public sealed record CheckoutProvinceOption(
    int ProvinceId,
    string ProvinceName,
    string? Code);

public sealed class CheckoutAddressValidationRequest
{
    [Required, StringLength(200, MinimumLength = 3)]
    public string AddressLine { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int ProvinceId { get; set; }

    [Range(1, int.MaxValue)]
    public int DistrictId { get; set; }

    [Required, StringLength(30, MinimumLength = 1)]
    [RegularExpression("^[A-Za-z0-9]+$")]
    public string WardCode { get; set; } = string.Empty;
}

public sealed class CheckoutAddressValidationViewModel
{
    public bool IsValid { get; init; }

    public string? ErrorCode { get; init; }

    public string? Message { get; init; }

    public int? ProvinceId { get; init; }

    public string? ProvinceName { get; init; }

    public int? DistrictId { get; init; }

    public string? DistrictName { get; init; }

    public string? WardCode { get; init; }

    public string? WardName { get; init; }
}
