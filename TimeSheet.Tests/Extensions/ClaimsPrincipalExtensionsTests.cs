using FluentAssertions;
using System.Security.Claims;
using TimeSheet.BLL.Exceptions;
using TimeSheet.PL.Extensions;

namespace TimeSheet.Tests.Extensions
{
    public class ClaimsPrincipalExtensionsTests
    {
        [Fact]
        public void GetUserId_ReturnsIdClaim()
        {
            var user = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("Id", "user-123"), new Claim("Role", "Employee")], "Bearer"));

            user.GetUserId().Should().Be("user-123");
        }

        [Fact]
        public void GetUserId_WithoutIdClaim_ThrowsUnauthorized()
        {
            var user = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("Role", "Employee")], "Bearer"));

            var act = () => user.GetUserId();

            act.Should().Throw<UnauthorizedException>();
        }
    }
}
