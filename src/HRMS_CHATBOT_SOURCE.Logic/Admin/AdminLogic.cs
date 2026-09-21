using System.ComponentModel.DataAnnotations;
using HRMS_CHATBOT_SOURCE.Domain.Dto.Request;
using HRMS_CHATBOT_SOURCE.Domain.Dto.Response;
using HRMS_CHATBOT_SOURCE.Domain.Dto.Settings;
using HRMS_CHATBOT_SOURCE.Domain.Interfaces;
using HRMS_CHATBOT_SOURCE.Infrastructure.Security;
using HRMS_CHATBOT_SOURCE.Logic.Adapter;
using HRMS_CHATBOT_SOURCE.Repo.Admin;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace HRMS_CHATBOT_SOURCE.Logic;

public class AdminLogic : IAdminLogic
{
    private readonly IUserProfileRepo _userProfileRepo;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly AppSettings _appSettings;

    public AdminLogic(
        IUserProfileRepo userProfileRepo,
        IJwtTokenService jwtTokenService,
        IHttpContextAccessor httpContextAccessor,
        IOptions<AppSettings> appSettings)
    {
        _userProfileRepo = userProfileRepo;
        _jwtTokenService = jwtTokenService;
        _httpContextAccessor = httpContextAccessor;
        _appSettings = appSettings.Value;
    }

    public async Task<LoginResponse?> ValidateLogin(
        LoginRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ValidationException("Invalid request.");
        }

        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            throw new ValidationException("User ID is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            throw new ValidationException("Password is required.");
        }

        ClearAuthCookie();

        var dbResponse = await _userProfileRepo.ValidateAdminLoginAsync(request, cancellationToken);
        var user = AdminAuthAdapter.MapValidateAdminLoginResponse(dbResponse);

        if (string.IsNullOrWhiteSpace(user.Mobile))
        {
            try
            {
                user.Mobile = await _userProfileRepo
                    .GetUserMobileByUserIdAsync(user.UserId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Optional lookup SP may not be deployed yet; login should still succeed.
            }
        }

        if (!user.IsActive)
        {
            throw new ValidationException("Your account is inactive. Please contact the administrator.");
        }

        if (user.ExitDate.HasValue && user.ExitDate.Value.Date <= DateTime.Today)
        {
            throw new ValidationException("Your account access has expired.");
        }

        if (!user.IsAdmin)
        {
            throw new ValidationException("You do not have admin access to this panel.");
        }

        await _userProfileRepo.UpdateLastAccessedAsync(user.UserId, cancellationToken);

        var token = _jwtTokenService.GenerateAccessToken(user, request.RememberMe);
        SetAuthCookie(token.AccessToken, request.RememberMe);

        return new LoginResponse
        {
            AccessToken = token.AccessToken,
            ExpiresIn = token.ExpiresInSeconds,
            User = new AdminUserDto
            {
                UserId = user.UserId,
                FullName = user.FullName,
                Department = user.Department,
                Designation = user.Designation,
                GroupCode = user.GroupCode,
                Email = user.Email,
                Mobile = user.Mobile
            }
        };
    }

    public LogoutResponse Logout()
    {
        ClearAuthCookie();
        return new LogoutResponse();
    }

    private void SetAuthCookie(string token, bool rememberMe)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return;
        }

        var expires = rememberMe
            ? DateTimeOffset.UtcNow.AddDays(_appSettings.RememberMeDays)
            : DateTimeOffset.UtcNow.AddMinutes(_appSettings.JwtAccessTokenExpiryInMin);
        AdminAuthCookieHelper.Append(httpContext, token, expires);
    }

    private void ClearAuthCookie()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext != null)
        {
            AdminAuthCookieHelper.Delete(httpContext);
        }
    }
}
