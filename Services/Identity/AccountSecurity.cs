using System.Globalization;
using System.Security.Claims;
using WebApplication2.Models.Enums;

namespace WebApplication2.Services.Identity;

public static class AccountSecurity
{
    public const string CustomerIdClaim = "fastbuy:customer_id";
    public const string SecurityStampClaim = "fastbuy:security_stamp";
    public const int MaximumFailedAccessAttempts = 5;

    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan AuthenticationLifetime = TimeSpan.FromDays(14);

    public static string NormalizeIdentity(string value) =>
        value.Trim().ToUpperInvariant();

    public static string RoleName(AccountRole role) => role switch
    {
        AccountRole.Admin => nameof(AccountRole.Admin),
        _ => nameof(AccountRole.Customer)
    };

    public static int? GetAccountId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;
    }

    public static int? GetCustomerId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(CustomerIdClaim);
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? id
            : null;
    }
}
