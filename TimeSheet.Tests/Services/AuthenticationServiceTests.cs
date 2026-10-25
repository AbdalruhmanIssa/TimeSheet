using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using TimeSheet.BLL.Services.Classes;
using TimeSheet.DAL.Repositories.Interface;
using TimeSheet.DAL.Utilities;
using static TimeSheet.Tests.TestHelpers;

namespace TimeSheet.Tests.Services
{
    // Covers the per-request token check (blocked / deleted / re-roled users).
    public class AuthenticationServiceTests
    {
        private readonly Mock<IUnitOfWork> _uow = new();
        private readonly AuthenticationService _sut;

        public AuthenticationServiceTests()
        {
            // SignInManager is only used by login, which these tests don't cover.
            _sut = new AuthenticationService(
                MockUserManager().Object, null!, new ConfigurationBuilder().Build(), _uow.Object);
        }

        [Fact]
        public async Task IsUserActiveAsync_ActiveUserWithSameRole_ReturnsTrue()
        {
            _uow.SetupIdentityData((NewUser("emp-1"), AppRoles.Employee));

            (await _sut.IsUserActiveAsync("emp-1", AppRoles.Employee)).Should().BeTrue();
        }

        [Fact]
        public async Task IsUserActiveAsync_BlockedUser_ReturnsFalse()
        {
            _uow.SetupIdentityData((NewUser("emp-1", isBlocked: true), AppRoles.Employee));

            (await _sut.IsUserActiveAsync("emp-1", AppRoles.Employee)).Should().BeFalse();
        }

        [Fact]
        public async Task IsUserActiveAsync_RoleChangedSinceTokenWasIssued_ReturnsFalse()
        {
            _uow.SetupIdentityData((NewUser("emp-1"), AppRoles.Manager));

            (await _sut.IsUserActiveAsync("emp-1", AppRoles.Employee)).Should().BeFalse();
        }

        [Fact]
        public async Task IsUserActiveAsync_DeletedUser_ReturnsFalse()
        {
            _uow.SetupIdentityData();

            (await _sut.IsUserActiveAsync("emp-1", AppRoles.Employee)).Should().BeFalse();
        }
    }
}
