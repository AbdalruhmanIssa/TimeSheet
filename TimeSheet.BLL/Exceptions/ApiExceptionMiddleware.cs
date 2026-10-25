using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TimeSheet.BLL.Exceptions
{
    public class ApiExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        public ApiExceptionMiddleware(RequestDelegate next) => _next = next;

        public async Task Invoke(
            HttpContext context,
            ILogger<ApiExceptionMiddleware> logger,
            IHostEnvironment env)
        {
            try
            {
                await _next(context);
            }
            catch (ApiException ex)
            {
                context.Response.StatusCode = ex.StatusCode;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new
                {
                    success = false,
                    message = ex.Message
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled exception for {Method} {Path}",
                    context.Request.Method, context.Request.Path);

                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/json";

                // Only surface the real exception text in Development.
                if (env.IsDevelopment())
                {
                    await context.Response.WriteAsJsonAsync(new
                    {
                        success = false,
                        message = "Unexpected server error.",
                        detail = ex.InnerException?.Message ?? ex.Message
                    });
                }
                else
                {
                    await context.Response.WriteAsJsonAsync(new
                    {
                        success = false,
                        message = "Unexpected server error."
                    });
                }
            }
        }
    }
}
