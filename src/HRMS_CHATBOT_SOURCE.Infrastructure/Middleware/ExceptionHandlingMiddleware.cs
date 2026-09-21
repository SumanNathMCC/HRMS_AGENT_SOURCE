using System.ComponentModel.DataAnnotations;
using System.Net;
using HRMS_CHATBOT_SOURCE.Domain.Dto.Response;
using HRMS_CHATBOT_SOURCE.Domain.Json;
using HRMS_CHATBOT_SOURCE.Infrastructure.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;

namespace HRMS_CHATBOT_SOURCE.Infrastructure.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext httpContext)
    {
        try
        {
            await _next(httpContext);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(httpContext, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        if (exception.InnerException != null)
        {
            exception = exception.InnerException;
        }

        var errorResponse = MapException(exception);
        _logger.LogError(exception, exception.Message);

        if (WantsJsonResponse(context))
        {
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = int.TryParse(errorResponse.ErrorCode, out var statusCode)
                ? statusCode
                : StatusCodes.Status500InternalServerError;

            context.Response.Headers["X-Request-TraceId"] = context.TraceIdentifier;
            context.Response.Headers["X-Request-ServedBy"] = "HRMS_CHATBOT_SOURCE";

            await context.Response.WriteAsync(
                JsonConvert.SerializeObject(errorResponse, DomainJsonSerializerSettings.Default));
            return;
        }

        var tempDataFactory = context.RequestServices.GetService<ITempDataDictionaryFactory>();
        if (tempDataFactory != null)
        {
            var tempData = tempDataFactory.GetTempData(context);
            tempData["ErrorMessage"] = errorResponse.ErrorMessage;
            tempData["ErrorCode"] = errorResponse.ErrorCode;
            tempData.Save();
        }

        var redirectPath = BuildErrorRedirectPath(context, errorResponse);

        if (errorResponse.ErrorCode == Convert.ToString((int)HttpStatusCode.Unauthorized))
        {
            AdminAuthCookieHelper.Delete(context);
        }

        if (string.IsNullOrWhiteSpace(redirectPath))
        {
            context.Response.StatusCode = int.TryParse(errorResponse.ErrorCode, out var statusCode)
                ? statusCode
                : StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsync(errorResponse.ErrorMessage ?? "An error occurred.");
            return;
        }

        if (IsSameRedirectPath(context, redirectPath))
        {
            if (!context.Request.Query.ContainsKey("session"))
            {
                context.Response.Redirect("/Admin/Account/Login?session=reset");
                return;
            }

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync(errorResponse.ErrorMessage ?? "Session expired. Please sign in again.");
            return;
        }

        context.Response.Redirect(redirectPath);
        return;
    }

    private static ErrorResponse MapException(Exception exception)
    {
        var errorResponse = new ErrorResponse();

        switch (exception)
        {
            case SecurityTokenException ex:
                errorResponse.ErrorMessage = ex.Message;
                errorResponse.ErrorCode = Convert.ToString((int)HttpStatusCode.Unauthorized);
                break;
            case UnauthorizedAccessException ex:
                errorResponse.ErrorMessage = ex.Message;
                errorResponse.ErrorCode = Convert.ToString((int)HttpStatusCode.Unauthorized);
                break;
            case HttpRequestException ex:
                errorResponse.ErrorMessage = ex.Message;
                errorResponse.ErrorCode = Convert.ToString(
                    ex.StatusCode != null ? (int)ex.StatusCode : (int)HttpStatusCode.BadRequest);
                break;
            case ValidationException ex:
                errorResponse.ErrorMessage = ex.Message;
                errorResponse.ErrorCode = Convert.ToString((int)HttpStatusCode.BadRequest);
                break;
            case ApplicationException ex when ex.Message.Contains("Invalid Token", StringComparison.OrdinalIgnoreCase):
                errorResponse.ErrorMessage = ex.Message;
                errorResponse.ErrorCode = Convert.ToString((int)HttpStatusCode.Unauthorized);
                break;
            case ApplicationException ex:
                errorResponse.ErrorMessage = ex.Message;
                errorResponse.ErrorCode = Convert.ToString((int)HttpStatusCode.BadRequest);
                break;
            default:
                if (exception.Message.Contains("Models.ValidationException", StringComparison.OrdinalIgnoreCase))
                {
                    errorResponse.ErrorCode = Convert.ToString((int)HttpStatusCode.BadRequest);
                }
                else
                {
                    errorResponse.ErrorCode = Convert.ToString((int)HttpStatusCode.InternalServerError);
                }

                errorResponse.ErrorMessage = exception.Message;
                break;
        }

        return errorResponse;
    }

    private static bool WantsJsonResponse(HttpContext context)
    {
        if (string.Equals(context.Request.ContentType, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (context.Request.Headers.TryGetValue("Accept", out var accept)
            && accept.Any(value => value?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true))
        {
            return true;
        }

        return context.Request.Path.StartsWithSegments("/api")
               || (context.Request.Path.StartsWithSegments("/Admin/Account/ValidateLogin")
                   && HttpMethods.IsPost(context.Request.Method));
    }

    private static string? BuildErrorRedirectPath(HttpContext context, ErrorResponse errorResponse)
    {
        if (!context.Request.Path.StartsWithSegments("/Admin"))
        {
            return null;
        }

        if (errorResponse.ErrorCode == Convert.ToString((int)HttpStatusCode.Unauthorized))
        {
            return "/Admin/Account/Login";
        }

        if (errorResponse.ErrorCode == Convert.ToString((int)HttpStatusCode.Forbidden))
        {
            return "/Admin/Account/AccessDenied";
        }

        return null;
    }

    private static bool IsSameRedirectPath(HttpContext context, string redirectPath)
    {
        var currentPath = context.Request.Path.Value?.TrimEnd('/') ?? string.Empty;
        var targetPath = redirectPath.TrimEnd('/');

        return string.Equals(currentPath, targetPath, StringComparison.OrdinalIgnoreCase);
    }
}

public static class ExceptionHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseHrmsExceptionHandler(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ExceptionHandlingMiddleware>();
    }
}
