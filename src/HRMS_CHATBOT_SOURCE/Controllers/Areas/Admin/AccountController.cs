using HRMS_CHATBOT_SOURCE.Domain.Dto.Request;
using HRMS_CHATBOT_SOURCE.Domain.Dto.Response;
using HRMS_CHATBOT_SOURCE.Domain.Dto.ViewModel;
using HRMS_CHATBOT_SOURCE.Infrastructure.Middleware;
using HRMS_CHATBOT_SOURCE.Infrastructure.Security;
using HRMS_CHATBOT_SOURCE.Logic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS_CHATBOT_SOURCE.Controllers.Areas.Admin;

[Area("Admin")]
public class AccountController : Controller
{
    private readonly IAdminLogic _adminLogic;

    public AccountController(IAdminLogic adminLogic)
    {
        _adminLogic = adminLogic;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (Request.Query.ContainsKey("session") &&
            string.Equals(Request.Query["session"], "reset", StringComparison.OrdinalIgnoreCase))
        {
            _adminLogic.Logout();
            AdminAuthCookieHelper.Delete(HttpContext);
            ViewData["ClearClientAuth"] = true;
        }
        else if (AdminAuthHelper.IsAuthenticatedAdmin(HttpContext))
        {
            return RedirectToAction("Index", "Dashboard", new { area = "Admin" });
        }

        if (Request.Cookies.ContainsKey("hrms_admin_token"))
        {
            AdminAuthCookieHelper.Delete(HttpContext);
            ViewData["ClearClientAuth"] = true;
        }

        var model = new LoginViewModel { ReturnUrl = returnUrl };
        if (TempData.TryGetValue("ErrorMessage", out var errorMessage))
        {
            ViewData["ErrorMessage"] = errorMessage?.ToString();
        }

        return View(model);
    }

    [HttpPost]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [Consumes("application/json")]
    [Produces("application/json")]
    public async Task<LoginResponse?> ValidateLogin(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        return await _adminLogic.ValidateLogin(request, cancellationToken);
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied()
    {
        ViewData["ErrorMessage"] = TempData["ErrorMessage"]?.ToString()
            ?? "You do not have permission to access the admin panel.";
        return View();
    }

    [HttpGet]
    [AdminAuthorize]
    public IActionResult Logout()
    {
        _adminLogic.Logout();
        return RedirectToAction(nameof(Login));
    }

    [HttpPost]
    [AdminAuthorize]
    [Produces("application/json")]
    public LogoutResponse LogoutPost()
    {
        return _adminLogic.Logout();
    }
}
