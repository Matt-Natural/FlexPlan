using System.Security.Claims;
using FlexPlan.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace FlexPlan.Services;

public static class AuthHelper
{
    public static async Task SignInAsync(HttpContext http, User user, bool isPersistent)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = isPersistent });

        SetThemeCookie(http, user.Theme);
    }

    public static void SetThemeCookie(HttpContext http, string theme) =>
        http.Response.Cookies.Append("fp-theme", theme.ToLowerInvariant(),
            new CookieOptions { Expires = DateTimeOffset.Now.AddYears(1), SameSite = SameSiteMode.Lax });
}
