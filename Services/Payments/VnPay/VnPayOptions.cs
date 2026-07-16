namespace WebApplication2.Services.Payments.VnPay;

public sealed class VnPayOptions
{
    public const string SectionName = "Integrations:Payments:VnPay";

    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "https://sandbox.vnpayment.vn";
    public string PaymentPath { get; set; } = "/paymentv2/vpcpay.html";
    public string TmnCode { get; set; } = string.Empty;
    public string HashSecret { get; set; } = string.Empty;
    public string ReturnUrl { get; set; } = string.Empty;
    public string IpnUrl { get; set; } = string.Empty;
    public string Version { get; set; } = "2.1.0";
    public string Locale { get; set; } = "vn";
    public string OrderType { get; set; } = "other";
    public int PaymentTimeoutMinutes { get; set; } = 15;
    public int ExpirationGraceMinutes { get; set; } = 5;
    public int ExpirationPollSeconds { get; set; } = 60;
    public int ExpirationBatchSize { get; set; } = 20;

    public bool IsConfigured =>
        Enabled
        && IsHostOnlyHttpsUrl(BaseUrl)
        && PaymentPath.StartsWith("/", StringComparison.Ordinal)
        && !PaymentPath.Contains("..", StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(TmnCode)
        && !string.IsNullOrWhiteSpace(HashSecret)
        && HashSecret.Trim().Length >= 16
        && IsHttpsUrl(ReturnUrl)
        && IsHttpsUrl(IpnUrl)
        && Version == "2.1.0"
        && Locale is "vn" or "en"
        && !string.IsNullOrWhiteSpace(OrderType)
        && PaymentTimeoutMinutes is >= 5 and <= 60
        && ExpirationGraceMinutes is >= 1 and <= 15
        && ExpirationPollSeconds is >= 15 and <= 300
        && ExpirationBatchSize is >= 1 and <= 100;

    private static bool IsHttpsUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps;
    }

    private static bool IsHostOnlyHttpsUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment)
            && (string.IsNullOrEmpty(uri.AbsolutePath) || uri.AbsolutePath == "/");
    }
}
