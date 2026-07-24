namespace WebApplication2.Services.Shipping.Ghn;

public sealed class GhnShippingOptions
{
    public const string SectionName = "Integrations:Shipping:Ghn";

    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "https://dev-online-gateway.ghn.vn";
    public string Token { get; set; } = string.Empty;
    public int ShopId { get; set; }
    public int FromDistrictId { get; set; }
    public string FromWardCode { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string SenderPhone { get; set; } = string.Empty;
    public string SenderAddress { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public string RequiredNote { get; set; } = "KHONGCHOXEMHANG";
    public int PaymentTypeId { get; set; } = 1;
    public int DefaultServiceTypeId { get; set; } = 2;
    public int DefaultWeightGram { get; set; } = 500;
    public int DefaultLengthCm { get; set; } = 20;
    public int DefaultWidthCm { get; set; } = 15;
    public int DefaultHeightCm { get; set; } = 10;
    public int WorkerPollSeconds { get; set; } = 10;
    public int WorkerBatchSize { get; set; } = 10;
    public int WorkerMaxAttempts { get; set; } = 8;
    public int WorkerLockMinutes { get; set; } = 5;
    public int TimeoutSeconds { get; set; } = 15;

    public bool IsQuoteConfigured =>
        Enabled
        && IsValidHostOnlyHttpsUrl(BaseUrl)
        && !string.IsNullOrWhiteSpace(Token)
        && ShopId > 0
        && FromDistrictId > 0
        && !string.IsNullOrWhiteSpace(FromWardCode)
        && DefaultServiceTypeId is 2 or 5
        && DefaultWeightGram is > 0 and <= 50_000
        && DefaultLengthCm is > 0 and <= 200
        && DefaultWidthCm is > 0 and <= 200
        && DefaultHeightCm is > 0 and <= 200
        && TimeoutSeconds is >= 5 and <= 60;

    // Existing quote/address clients check IsConfigured before sending.
    // In this architecture GHN is enabled only for service lookup and fees.
    public bool IsConfigured => IsQuoteConfigured;

    public bool IsExecutionConfigured =>
        IsQuoteConfigured
        && !string.IsNullOrWhiteSpace(SenderName)
        && !string.IsNullOrWhiteSpace(SenderPhone)
        && !string.IsNullOrWhiteSpace(SenderAddress)
        && WebhookSecret.Trim().Length >= 16
        && RequiredNote is "CHOTHUHANG" or "CHOXEMHANGKHONGTHU" or "KHONGCHOXEMHANG"
        && PaymentTypeId is 1 or 2
        && WorkerPollSeconds is >= 2 and <= 300
        && WorkerBatchSize is >= 1 and <= 100
        && WorkerMaxAttempts is >= 1 and <= 20
        && WorkerLockMinutes is >= 1 and <= 60;

    private static bool IsValidHostOnlyHttpsUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment)
            && (string.IsNullOrEmpty(uri.AbsolutePath) || uri.AbsolutePath == "/");
    }
}
