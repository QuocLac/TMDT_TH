using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using WebApplication2.Models.Enums;

namespace WebApplication2.Services.Identity;

public sealed class AdminAreaAuthorizationFilter : IAsyncAuthorizationFilter
{
    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var area = context.RouteData.Values["area"]?.ToString();
        if (!string.Equals(area, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
        {
            var returnUrl = context.HttpContext.Request.PathBase
                + context.HttpContext.Request.Path
                + context.HttpContext.Request.QueryString;

            context.Result = new ChallengeResult(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new AuthenticationProperties { RedirectUri = returnUrl });
            return Task.CompletedTask;
        }

        if (!context.HttpContext.User.IsInRole(nameof(AccountRole.Admin)))
        {
            context.Result = new ForbidResult(
                CookieAuthenticationDefaults.AuthenticationScheme);
        }

        return Task.CompletedTask;
    }
}
