namespace WebApplication2.Services.Shipping.Ghn;

public sealed class GhnAddressOptions
{
    public const string SectionName = "Integrations:Shipping:Ghn";

    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "https://dev-online-gateway.ghn.vn";

    public string Token { get; set; } = string.Empty;

    public int ShopId { get; set; }

    public int FromDistrictId { get; set; }

    public string FromWardCode { get; set; } = string.Empty;

    public int ProvinceCacheMinutes { get; set; } = 1_440;

    public int DistrictCacheMinutes { get; set; } = 720;

    public int WardCacheMinutes { get; set; } = 720;

    public int TimeoutSeconds { get; set; } = 15;

    public bool IsConfigured =>
        Enabled
        && IsValidHostOnlyHttpsUrl(BaseUrl)
        && !string.IsNullOrWhiteSpace(Token)
        && ShopId > 0
        && FromDistrictId > 0
        && !string.IsNullOrWhiteSpace(FromWardCode)
        && TimeoutSeconds is >= 5 and <= 60;

    private static bool IsValidHostOnlyHttpsUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment)
            && (string.IsNullOrEmpty(uri.AbsolutePath) || uri.AbsolutePath == "/");
    }
}
