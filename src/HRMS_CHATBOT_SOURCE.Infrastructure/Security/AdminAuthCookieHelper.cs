using Microsoft.AspNetCore.Http;

namespace HRMS_CHATBOT_SOURCE.Infrastructure.Security;

public static class AdminAuthCookieHelper
{
    public const string CookieName = "hrms_admin_token";

    public static CookieOptions CreateOptions(HttpContext httpContext, DateTimeOffset? expires = null)
    {
        return new CookieOptions
        {
            HttpOnly = false,
            Secure = httpContext.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = expires
        };
    }

    public static void Delete(HttpContext httpContext)
    {
        httpContext.Response.Cookies.Delete(CookieName, CreateOptions(httpContext));
    }

    public static void Append(HttpContext httpContext, string token, DateTimeOffset expires)
    {
        httpContext.Response.Cookies.Append(CookieName, token, CreateOptions(httpContext, expires));
    }
}
