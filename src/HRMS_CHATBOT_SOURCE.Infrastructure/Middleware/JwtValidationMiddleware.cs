using System.Security.Claims;
using HRMS_CHATBOT_SOURCE.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace HRMS_CHATBOT_SOURCE.Infrastructure.Middleware;

public sealed class JwtValidationMiddleware
{
    private readonly RequestDelegate _next;

    public JwtValidationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, JwtTokenValidator tokenValidator)
    {
        var path = context.Request.Path;
        if (ShouldSkip(path))
        {
            await _next(context);
            return;
        }

        var endpoint = context.GetEndpoint();
        if (endpoint == null)
        {
            await _next(context);
            return;
        }

        var allowAnonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() != null;
        var rawToken = JwtTokenValidator.ReadRawToken(context);
        var principal = !string.IsNullOrWhiteSpace(rawToken)
            ? tokenValidator.TryValidate(rawToken)
            : null;

        if (principal != null)
        {
            context.User = CreateAuthenticatedPrincipal(principal);
            await _next(context);
            return;
        }

        if (allowAnonymous)
        {
            await _next(context);
            return;
        }

        if (!string.IsNullOrWhiteSpace(rawToken))
        {
            if (ShouldRedirectInvalidTokenToLogin(context))
            {
                AdminAuthCookieHelper.Delete(context);
                context.Response.Redirect("/Admin/Account/Login?session=reset");
                return;
            }

            await WriteUnauthorizedAsync(context, "Invalid Token");
            return;
        }

        if (path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            await WriteUnauthorizedAsync(context, "Unauthorized");
            return;
        }

        if (path.StartsWithSegments("/Admin", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Redirect("/Admin/Account/Login");
            return;
        }

        await WriteUnauthorizedAsync(context, "Unauthorized");
    }

    private static Task WriteUnauthorizedAsync(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return context.Response.WriteAsync(message);
    }

    private static bool ShouldSkip(PathString path)
    {
        if (path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
            || path == "/")
        {
            return true;
        }

        if (path.StartsWithSegments("/Admin/Account/ValidateLogin", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/Admin/Account/Login", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/Admin/Account/AccessDenied", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool ShouldRedirectInvalidTokenToLogin(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/Admin", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (context.Request.Headers.TryGetValue("Accept", out var accept)
            && accept.Any(value => value?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
            && !accept.Any(value => value?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true))
        {
            return false;
        }

        return HttpMethods.IsGet(context.Request.Method)
            || HttpMethods.IsHead(context.Request.Method);
    }

    private static ClaimsPrincipal CreateAuthenticatedPrincipal(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated == true)
        {
            return principal;
        }

        return new ClaimsPrincipal(new ClaimsIdentity(principal.Claims, "Bearer"));
    }
}

public static class JwtValidationMiddlewareExtensions
{
    public static IApplicationBuilder UseJwtValidationMiddleware(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<JwtValidationMiddleware>();
    }
}
