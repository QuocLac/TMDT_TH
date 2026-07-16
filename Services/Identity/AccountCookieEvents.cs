using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using WebApplication2.Models;

namespace WebApplication2.Services.Identity;

public sealed class AccountCookieEvents : CookieAuthenticationEvents
{
    private readonly ApplicationDbContext _context;
    private readonly TimeProvider _timeProvider;

    public AccountCookieEvents(ApplicationDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var accountId = context.Principal?.GetAccountId();
        var securityStamp = context.Principal?
            .FindFirst(AccountSecurity.SecurityStampClaim)?
            .Value;

        if (!accountId.HasValue || string.IsNullOrWhiteSpace(securityStamp))
        {
            await RejectAsync(context);
            return;
        }

        var account = await _context.Accounts
            .AsNoTracking()
            .Where(item => item.Id == accountId.Value)
            .Select(item => new
            {
                item.IsActive,
                item.LockoutEndAt,
                item.SecurityStamp
            })
            .SingleOrDefaultAsync(context.HttpContext.RequestAborted);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        if (account is null
            || !account.IsActive
            || (account.LockoutEndAt.HasValue && account.LockoutEndAt.Value > nowUtc)
            || !string.Equals(account.SecurityStamp, securityStamp, StringComparison.Ordinal))
        {
            await RejectAsync(context);
        }
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(
            CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
