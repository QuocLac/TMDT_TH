namespace WebApplication2.Services.Payments.VnPay;

public sealed class VnPayOptions
{
    public const string SectionName = "Integrations:Payments:VnPay";

    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "https://sandbox.vnpayment.vn";

    public string TmnCode { get; set; } = string.Empty;

    public string HashSecret { get; set; } = string.Empty;

    public string ReturnUrl { get; set; } = string.Empty;

    public string IpnUrl { get; set; } = string.Empty;

    public bool IsConfigured =>
        Enabled
        && IsHttpsUrl(BaseUrl)
        && !string.IsNullOrWhiteSpace(TmnCode)
        && !string.IsNullOrWhiteSpace(HashSecret)
        && IsHttpsUrl(ReturnUrl)
        && IsHttpsUrl(IpnUrl);

    private static bool IsHttpsUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps;
    }
}
