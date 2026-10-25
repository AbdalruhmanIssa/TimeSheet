using System.Security.Claims;
using TimeSheet.BLL.Exceptions;

namespace TimeSheet.PL.Extensions
{
    public static class ClaimsPrincipalExtensions
    {
        // Our JWTs put the user id in a custom "Id" claim (see AuthenticationService).
        public static string GetUserId(this ClaimsPrincipal user)
        {
            var id = user.FindFirst("Id")?.Value;

            if (string.IsNullOrWhiteSpace(id))
                throw new UnauthorizedException("User id claim is missing from the token.");

            return id;
        }
    }
}
