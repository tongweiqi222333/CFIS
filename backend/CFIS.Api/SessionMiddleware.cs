using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
namespace CFIS.Api;
// Validate Identity security stamps on every bearer request; logout revokes all sessions.
public sealed class SessionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, UserManager<IdentityUser> users)
    {
        // Public credential recovery must remain usable when a browser still sends a revoked bearer.
        if (new[] { "/api/v1/auth/login", "/api/v1/auth/register", "/api/v1/auth/forgotPassword", "/api/v1/auth/resetPassword", "/api/v1/auth/refresh" }.Contains(context.Request.Path.Value, StringComparer.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var user = await users.GetUserAsync(context.User);
            var stamp = context.User.FindFirst("AspNet.Identity.SecurityStamp")?.Value;
            if (user is null || stamp != await users.GetSecurityStampAsync(user))
            {
                context.Response.StatusCode = 401;
                return;
            }
        }
        await next(context);
    }
}
