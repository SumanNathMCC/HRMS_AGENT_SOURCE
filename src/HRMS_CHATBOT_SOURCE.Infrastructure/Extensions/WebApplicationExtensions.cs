using HRMS_CHATBOT_SOURCE.Infrastructure.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace HRMS_CHATBOT_SOURCE.Infrastructure.Extensions;

public static class WebApplicationExtensions
{
    public static WebApplication UseHrmsPipeline(this WebApplication app)
    {
        app.UseHrmsExceptionHandler();
        app.UseHrmsTraceHeader();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }
        else
        {
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseStaticFiles();
        app.UseRouting();
        app.UseCors("HrmsCorsPolicy");
        app.UseSession();
        app.UseJwtValidationMiddleware();

        app.MapControllers();
        app.MapControllerRoute(
            name: "areas",
            pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");
        app.MapGet("/", () => Results.Redirect("/Admin/Account/Login"))
            .AllowAnonymous();
        app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
            .AllowAnonymous();

        return app;
    }
}
