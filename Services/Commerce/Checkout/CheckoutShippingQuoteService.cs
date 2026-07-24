using Microsoft.Extensions.Options;
using WebApplication2.Services.Commerce.Orders;
using WebApplication2.Services.Shipping;
using WebApplication2.Services.Shipping.Ghn;

namespace WebApplication2.Services.Commerce.Checkout;

public sealed record CheckoutShippingLine(
    int VariantId,
    string ProductName,
    string Sku,
    int Quantity,
    decimal UnitPrice);

public sealed record CheckoutShippingQuoteCommand(
    int ToDistrictId,
    string ToWardCode,
    string PaymentMethod,
    IReadOnlyCollection<CheckoutShippingLine> Lines);

public sealed record CheckoutShippingQuoteResult(
    bool Success,
    decimal Fee,
    int ServiceId,
    int ServiceTypeId,
    string ServiceName,
    int WeightGram,
    int LengthCm,
    int WidthCm,
    int HeightCm,
    bool IsFallback,
    string? ErrorCode,
    string Message,
    string? RawResponse)
{
    public static CheckoutShippingQuoteResult Failure(
        string errorCode,
        string message) =>
        new(
            false,
            0m,
            0,
            0,
            string.Empty,
            0,
            0,
            0,
            0,
            false,
            errorCode,
            message,
            null);
}

public interface ICheckoutShippingQuoteService
{
    Task<CheckoutShippingQuoteResult> QuoteAsync(
        CheckoutShippingQuoteCommand command,
        CancellationToken cancellationToken);
}

public sealed class CheckoutShippingQuoteService : ICheckoutShippingQuoteService
{
    private readonly IGhnShippingClient _ghnShippingClient;
    private readonly GhnShippingOptions _options;
    private readonly ILogger<CheckoutShippingQuoteService> _logger;

    public CheckoutShippingQuoteService(
        IGhnShippingClient ghnShippingClient,
        IOptions<GhnShippingOptions> options,
        ILogger<CheckoutShippingQuoteService> logger)
    {
        _ghnShippingClient = ghnShippingClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<CheckoutShippingQuoteResult> QuoteAsync(
        CheckoutShippingQuoteCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var lines = command.Lines?
            .Where(line =>
                line.VariantId > 0
                && line.Quantity > 0
                && line.UnitPrice > 0)
            .OrderBy(line => line.VariantId)
            .ToArray()
            ?? [];

        if (command.ToDistrictId <= 0
            || string.IsNullOrWhiteSpace(command.ToWardCode))
        {
            return CheckoutShippingQuoteResult.Failure(
                "INVALID_DESTINATION",
                "Vui lòng chọn đầy đủ quận/huyện và phường/xã nhận hàng.");
        }

        if (lines.Length == 0)
        {
            return CheckoutShippingQuoteResult.Failure(
                "EMPTY_SHIPPING_CART",
                "Không có sản phẩm hợp lệ để tính phí giao hàng.");
        }

        var subtotal = lines.Sum(line => line.UnitPrice * line.Quantity);
        var totalQuantity = lines.Sum(line => line.Quantity);

        if (!_options.IsQuoteConfigured)
        {
            return CheckoutShippingQuoteResult.Failure(
                "SHIPPING_QUOTE_NOT_CONFIGURED",
                "Chưa thể tính phí giao hàng tự động. Vui lòng thử lại sau.");
        }

        var services = await _ghnShippingClient.GetAvailableServicesAsync(
            command.ToDistrictId,
            cancellationToken);

        if (!services.Success || services.Data is null || services.Data.Count == 0)
        {
            _logger.LogWarning(
                "Shipping service lookup failed. ErrorCode={ErrorCode}; Message={Message}",
                services.ErrorCode,
                services.Message);

            return CheckoutShippingQuoteResult.Failure(
                "SHIPPING_SERVICE_UNAVAILABLE",
                "Chưa tìm thấy hình thức giao hàng phù hợp cho địa chỉ đã chọn.");
        }

        var selectedService = services.Data
            .Where(item => item.ServiceId > 0 && item.ServiceTypeId > 0)
            .OrderByDescending(item =>
                item.ServiceTypeId == _options.DefaultServiceTypeId)
            .ThenBy(item => item.ServiceTypeId)
            .ThenBy(item => item.ServiceId)
            .FirstOrDefault();

        if (selectedService is null)
        {
            return CheckoutShippingQuoteResult.Failure(
                "SHIPPING_SERVICE_NOT_FOUND",
                "Chưa tìm thấy hình thức giao hàng phù hợp cho địa chỉ đã chọn.");
        }

        var weightGram = CalculateWeight(totalQuantity);
        var parcelItems = lines
            .Select(line => new ShippingParcelItem(
                Limit(line.ProductName, 200),
                Limit(line.Sku, 50),
                line.Quantity,
                ToProviderMoney(line.UnitPrice, 50_000_000),
                _options.DefaultWeightGram,
                _options.DefaultLengthCm,
                _options.DefaultWidthCm,
                _options.DefaultHeightCm))
            .ToArray();

        var baseCodAmount = string.Equals(
            command.PaymentMethod,
            OrderApplicationService.CodPaymentMethod,
            StringComparison.Ordinal)
            ? ToProviderMoney(subtotal, 50_000_000)
            : 0;

        var firstQuote = await QuoteProviderAsync(
            selectedService,
            command,
            subtotal,
            baseCodAmount,
            weightGram,
            parcelItems,
            cancellationToken);

        if (!firstQuote.Success || firstQuote.Data is null)
        {
            _logger.LogWarning(
                "Automatic shipping quote failed. ErrorCode={ErrorCode}; Message={Message}",
                firstQuote.ErrorCode,
                firstQuote.Message);

            return CheckoutShippingQuoteResult.Failure(
                "SHIPPING_QUOTE_FAILED",
                "Chưa thể tính phí giao hàng cho địa chỉ này. Vui lòng thử lại.");
        }

        var effectiveQuote = firstQuote.Data;

        // COD includes merchandise and the shipping fee. A second quote keeps
        // the charge authoritative without exposing the provider lifecycle.
        if (baseCodAmount > 0 && effectiveQuote.TotalFee > 0)
        {
            var finalCodAmount = ToProviderMoney(
                subtotal + effectiveQuote.TotalFee,
                50_000_000);

            if (finalCodAmount != baseCodAmount)
            {
                var secondQuote = await QuoteProviderAsync(
                    selectedService,
                    command,
                    subtotal,
                    finalCodAmount,
                    weightGram,
                    parcelItems,
                    cancellationToken);

                if (secondQuote.Success && secondQuote.Data is not null)
                {
                    effectiveQuote = secondQuote.Data;
                }
                else
                {
                    _logger.LogWarning(
                        "Second-pass COD shipping quote failed with code {ErrorCode}. The first valid quote is retained.",
                        secondQuote.ErrorCode);
                }
            }
        }

        return new CheckoutShippingQuoteResult(
            true,
            effectiveQuote.TotalFee,
            selectedService.ServiceId,
            selectedService.ServiceTypeId,
            CustomerServiceName(selectedService.ServiceTypeId),
            weightGram,
            _options.DefaultLengthCm,
            _options.DefaultWidthCm,
            _options.DefaultHeightCm,
            false,
            null,
            "Phí giao hàng đã được tính tự động theo địa chỉ nhận hàng.",
            effectiveQuote.RawResponse);
    }

    private Task<ShippingOperationResult<ShippingQuote>> QuoteProviderAsync(
        ShippingServiceOption service,
        CheckoutShippingQuoteCommand command,
        decimal subtotal,
        int codAmount,
        int weightGram,
        IReadOnlyList<ShippingParcelItem> parcelItems,
        CancellationToken cancellationToken)
    {
        return _ghnShippingClient.QuoteAsync(
            new ShippingQuoteRequest(
                _options.FromDistrictId,
                _options.FromWardCode,
                command.ToDistrictId,
                command.ToWardCode.Trim(),
                service.ServiceId,
                service.ServiceTypeId,
                weightGram,
                _options.DefaultLengthCm,
                _options.DefaultWidthCm,
                _options.DefaultHeightCm,
                ToProviderMoney(subtotal, 5_000_000),
                codAmount,
                parcelItems),
            cancellationToken);
    }

    private int CalculateWeight(int totalQuantity)
    {
        var perUnit = Math.Max(1, _options.DefaultWeightGram);
        return (int)Math.Clamp(
            (long)perUnit * totalQuantity,
            1L,
            1_600_000L);
    }

    private static string CustomerServiceName(int serviceTypeId) =>
        serviceTypeId switch
        {
            5 => "Giao hàng cồng kềnh",
            _ => "Giao hàng nhanh"
        };

    private static int ToProviderMoney(decimal value, int maximum)
    {
        var rounded = decimal.Round(
            Math.Max(0m, value),
            0,
            MidpointRounding.AwayFromZero);

        return (int)Math.Clamp(
            rounded,
            0m,
            maximum);
    }

    private static string Limit(string? value, int maximumLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? "Sản phẩm"
            : value.Trim();

        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength];
    }
}
