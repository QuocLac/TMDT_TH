using System.ComponentModel.DataAnnotations;
using WebApplication2.Models.Enums;
using WebApplication2.Services.Commerce.Checkout;
using WebApplication2.Services.Commerce.Orders;
using WebApplication2.ViewModels.Storefront.Cart;

namespace WebApplication2.ViewModels.Storefront.Checkout;

public sealed class CheckoutPageViewModel
{
    public string ClientRequestId { get; init; } = string.Empty;
    public long CartVersion { get; init; }
    public MockCustomerViewModel Customer { get; init; } = new();
    public IReadOnlyList<CheckoutLineViewModel> Items { get; init; } = [];
    public decimal Subtotal { get; init; }
    public CheckoutShippingQuoteViewModel ShippingQuote { get; init; } = new();
    public decimal GrandTotal =>
        Subtotal + (ShippingQuote.IsAvailable ? ShippingQuote.Fee : 0m);
    public IReadOnlyList<CheckoutProvinceOption> Provinces { get; init; } = [];
    public bool VnPayAvailable { get; init; }
    public string? AddressLookupError { get; init; }
    public string? ErrorMessage { get; init; }
    public CheckoutPlaceOrderRequest Form { get; init; } = new();

    public static CheckoutPageViewModel FromCart(
        CartPageViewModel cart,
        long cartVersion,
        string clientRequestId,
        CheckoutShippingQuoteResult? shippingQuote,
        IReadOnlyList<CheckoutProvinceOption> provinces,
        bool vnPayAvailable,
        CheckoutPlaceOrderRequest? form = null,
        string? addressLookupError = null,
        string? errorMessage = null)
    {
        var items = cart.Items
            .Where(item => item.IsSelected && item.CanSelect)
            .Select(item => new CheckoutLineViewModel
            {
                ProductId = item.ProductId,
                VariantId = item.VariantId,
                ProductName = item.ProductName,
                VariantDescription = item.VariantDescription,
                Sku = item.Sku,
                Quantity = item.Quantity,
                UnitPrice = item.EffectivePrice,
                LineTotal = item.LineTotal,
                ImageUrl = item.ImageUrl
            })
            .ToArray();

        var quoteViewModel = CheckoutShippingQuoteViewModel.FromResult(
            shippingQuote);

        var checkoutForm = form ?? new CheckoutPlaceOrderRequest
        {
            ClientRequestId = clientRequestId,
            CartVersion = cartVersion,
            FullName = cart.Customer.FullName,
            Email = cart.Customer.Email,
            Phone = cart.Customer.Phone,
            AddressLine = cart.Customer.AddressLine,
            PaymentMethod = OrderApplicationService.CodPaymentMethod,
            Items = items.Select(item =>
                new CheckoutItemConfirmationRequest
                {
                    VariantId = item.VariantId,
                    Quantity = item.Quantity,
                    ExpectedUnitPrice = item.UnitPrice
                })
                .ToList()
        };

        checkoutForm.ExpectedShippingFee = quoteViewModel.Fee;
        checkoutForm.ShippingServiceId = quoteViewModel.ServiceId;
        checkoutForm.ShippingServiceTypeId = quoteViewModel.ServiceTypeId;

        return new CheckoutPageViewModel
        {
            ClientRequestId = clientRequestId,
            CartVersion = cartVersion,
            Customer = cart.Customer,
            Items = items,
            Subtotal = items.Sum(item => item.LineTotal),
            ShippingQuote = quoteViewModel,
            Provinces = provinces,
            VnPayAvailable = vnPayAvailable,
            AddressLookupError = addressLookupError,
            ErrorMessage = errorMessage,
            Form = checkoutForm
        };
    }
}

public sealed class CheckoutShippingQuoteViewModel
{
    public bool IsAvailable { get; init; }
    public decimal Fee { get; init; }
    public int ServiceId { get; init; }
    public int ServiceTypeId { get; init; }
    public string ServiceName { get; init; } = string.Empty;
    public bool IsFallback { get; init; }
    public string Message { get; init; } =
        "Chọn địa chỉ để tính phí giao hàng.";

    public static CheckoutShippingQuoteViewModel FromResult(
        CheckoutShippingQuoteResult? result)
    {
        if (result is null || !result.Success)
        {
            return new CheckoutShippingQuoteViewModel
            {
                Message = result?.Message
                    ?? "Chọn địa chỉ để tính phí giao hàng."
            };
        }

        return new CheckoutShippingQuoteViewModel
        {
            IsAvailable = true,
            Fee = result.Fee,
            ServiceId = result.ServiceId,
            ServiceTypeId = result.ServiceTypeId,
            ServiceName = result.ServiceName,
            IsFallback = result.IsFallback,
            Message = result.Message
        };
    }
}

public sealed class CheckoutLineViewModel
{
    public int ProductId { get; init; }
    public int VariantId { get; init; }
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

public sealed class CheckoutShippingQuoteRequest
{
    [Range(0, long.MaxValue)]
    public long CartVersion { get; set; }

    [Range(1, int.MaxValue)]
    public int DistrictId { get; set; }

    [Required, StringLength(30)]
    [RegularExpression("^[A-Za-z0-9]+$")]
    public string WardCode { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^(COD|VNPAY)$")]
    public string PaymentMethod { get; set; } =
        OrderApplicationService.CodPaymentMethod;
}

public sealed class CheckoutShippingQuoteResponseViewModel
{
    public decimal Fee { get; init; }
    public decimal GrandTotal { get; init; }
    public int ServiceId { get; init; }
    public int ServiceTypeId { get; init; }
    public string ServiceName { get; init; } = string.Empty;
    public bool IsFallback { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class CheckoutPlaceOrderRequest
{
    [Required, StringLength(32, MinimumLength = 32)]
    [RegularExpression("^[a-fA-F0-9]{32}$")]
    public string ClientRequestId { get; set; } = string.Empty;

    [Range(0, long.MaxValue)]
    public long CartVersion { get; set; }

    [Required, StringLength(100, MinimumLength = 2)]
    public string FullName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(150)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(20, MinimumLength = 9)]
    [RegularExpression("^[0-9]{9,15}$")]
    public string Phone { get; set; } = string.Empty;

    [Required, StringLength(200, MinimumLength = 3)]
    public string AddressLine { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int ProvinceId { get; set; }

    [Range(1, int.MaxValue)]
    public int DistrictId { get; set; }

    [Required, StringLength(30)]
    [RegularExpression("^[A-Za-z0-9]+$")]
    public string WardCode { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^(COD|VNPAY)$")]
    public string PaymentMethod { get; set; } =
        OrderApplicationService.CodPaymentMethod;

    [Range(typeof(decimal), "0", "9999999999999999")]
    public decimal ExpectedShippingFee { get; set; }

    [Range(0, int.MaxValue)]
    public int ShippingServiceId { get; set; }

    [Range(0, int.MaxValue)]
    public int ShippingServiceTypeId { get; set; }

    [MinLength(1)]
    public List<CheckoutItemConfirmationRequest> Items { get; set; } = [];
}

public sealed class CheckoutItemConfirmationRequest
{
    [Range(1, int.MaxValue)]
    public int VariantId { get; set; }

    [Range(1, 99)]
    public int Quantity { get; set; }

    [Range(typeof(decimal), "0.01", "9999999999999999")]
    public decimal ExpectedUnitPrice { get; set; }
}

public sealed class CheckoutSuccessViewModel
{
    public string OrderCode { get; init; } = string.Empty;
    public Guid PublicToken { get; init; }
    public OrderStatus OrderStatus { get; init; }
    public PaymentStatus PaymentStatus { get; init; }
    public FulfillmentStatus FulfillmentStatus { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string CustomerEmail { get; init; } = string.Empty;
    public string CustomerPhone { get; init; } = string.Empty;
    public string ShippingAddress { get; init; } = string.Empty;
    public decimal Subtotal { get; init; }
    public decimal ShippingFee { get; init; }
    public decimal GrandTotal { get; init; }
    public string Currency { get; init; } = "VND";
    public DateTime CreatedAt { get; init; }
    public IReadOnlyList<OrderReceiptLine> Items { get; init; } = [];

    public static CheckoutSuccessViewModel FromReceipt(OrderReceipt receipt)
    {
        return new CheckoutSuccessViewModel
        {
            OrderCode = receipt.OrderCode,
            PublicToken = receipt.PublicToken,
            OrderStatus = receipt.OrderStatus,
            PaymentStatus = receipt.PaymentStatus,
            FulfillmentStatus = receipt.FulfillmentStatus,
            CustomerName = receipt.CustomerName,
            CustomerEmail = receipt.CustomerEmail,
            CustomerPhone = receipt.CustomerPhone,
            ShippingAddress = receipt.ShippingAddress,
            Subtotal = receipt.Subtotal,
            ShippingFee = receipt.ShippingFee,
            GrandTotal = receipt.GrandTotal,
            Currency = receipt.Currency,
            CreatedAt = receipt.CreatedAt,
            Items = receipt.Items
        };
    }
}
