using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Moq;
using TimeSheet.BLL.DTOs.Request;
using TimeSheet.BLL.Exceptions;
using TimeSheet.BLL.Services.Classes;
using TimeSheet.DAL.Enums;
using TimeSheet.DAL.Models;
using TimeSheet.DAL.Repositories.Interface;
using TimeSheet.DAL.Utilities;
using static TimeSheet.Tests.TestHelpers;

namespace TimeSheet.Tests.Services
{
    public class UserServiceTests
    {
        private const string AdminId = "admin-1";

        private readonly Mock<IUnitOfWork> _uow = new();
        private readonly Mock<UserManager<ApplicationUser>> _userManager = MockUserManager();
        private readonly UserService _sut;

        public UserServiceTests()
        {
            _sut = new UserService(_userManager.Object, _uow.Object);
        }

        // ---------------------------------------------------------------- Get

        [Fact]
        public async Task GetAllAsync_FiltersByRole_IgnoringCase()
        {
            _uow.SetupIdentityData(
                (NewUser("emp-1", "Bob"), AppRoles.Employee),
                (NewUser("mgr-1", "Alice"), AppRoles.Manager),
                (NewUser("emp-2", "Carl"), AppRoles.Employee));

            var result = await _sut.GetAllAsync(new UserFilterRequest { Role = "employee" });

            result.Select(u => u.Id).Should().Equal("emp-1", "emp-2"); // ordered by full name
            result.Should().OnlyContain(u => u.Role == AppRoles.Employee);
        }

        [Fact]
        public async Task GetByIdAsync_WhenMissing_ThrowsNotFound()
        {
            _uow.SetupIdentityData();

            var act = () => _sut.GetByIdAsync("ghost");

            await act.Should().ThrowAsync<NotFoundException>().WithMessage("User not found.");
        }

        // ---------------------------------------------------------------- Create

        [Fact]
        public async Task CreateAsync_WithUnknownRole_ThrowsBadRequest_AndCreatesNothing()
        {
            var act = () => _sut.CreateAsync(ValidCreateRequest(role: "SuperAdmin"));

            await act.Should().ThrowAsync<BadRequestException>().WithMessage("Unknown role*");
            _userManager.Verify(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task CreateAsync_CreatesUserAndAssignsCanonicalRole()
        {
            var data = _uow.SetupIdentityData();
            _uow.SetupTransaction<string>();

            _userManager.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), "Pass@1234"))
                .Callback<ApplicationUser, string>((u, _) => { u.Id = "new-1"; data.Users.Add(u); })
                .ReturnsAsync(IdentityResult.Success);
            _userManager.Setup(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), AppRoles.Employee))
                .Callback<ApplicationUser, string>((u, r) =>
                    data.UserRoles.Add(new IdentityUserRole<string> { UserId = u.Id, RoleId = RoleId(r) }))
                .ReturnsAsync(IdentityResult.Success);

            var result = await _sut.CreateAsync(ValidCreateRequest(role: "employee"));

            result.Id.Should().Be("new-1");
            result.Role.Should().Be(AppRoles.Employee);
            result.FullName.Should().Be("Sara Haddad"); // trimmed
            data.Users.Single().EmailConfirmed.Should().BeTrue();
        }

        [Fact]
        public async Task CreateAsync_WhenIdentityRejects_ThrowsBadRequestWithIdentityMessage()
        {
            _uow.SetupTransaction<string>();
            _userManager.Setup(m => m.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
                .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Email 'x' is already taken." }));

            var act = () => _sut.CreateAsync(ValidCreateRequest());

            await act.Should().ThrowAsync<BadRequestException>().WithMessage("Email 'x' is already taken.");
            _userManager.Verify(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
        }

        // ---------------------------------------------------------------- Role

        [Fact]
        public async Task ChangeRoleAsync_OnSelf_ThrowsBadRequest()
        {
            var act = () => _sut.ChangeRoleAsync(AdminId, AdminId, new ChangeUserRoleRequest { Role = AppRoles.Employee });

            await act.Should().ThrowAsync<BadRequestException>().WithMessage("You cannot change your own role.");
        }

        [Fact]
        public async Task ChangeRoleAsync_WhenUserManagesAProject_ThrowsBadRequest()
        {
            var user = SetupUserForRoleChange("mgr-1", AppRoles.Manager);
            _uow.SetupRepo(new List<Project> { new() { Code = "PRJ-0001", DirectorId = "dir-1", ManagerId = user.Id } });
            _uow.SetupRepo(new List<ProjectAssignment>());

            var act = () => _sut.ChangeRoleAsync(AdminId, user.Id, new ChangeUserRoleRequest { Role = AppRoles.Employee });

            await act.Should().ThrowAsync<BadRequestException>().WithMessage("*before changing the role.");
            _userManager.Verify(m => m.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ChangeRoleAsync_WhenEmployeeIsAssigned_ThrowsBadRequest()
        {
            var user = SetupUserForRoleChange("emp-1", AppRoles.Employee);
            _uow.SetupRepo(new List<Project>());
            _uow.SetupRepo(new List<ProjectAssignment> { new() { EmployeeId = user.Id, IsActive = true } });

            var act = () => _sut.ChangeRoleAsync(AdminId, user.Id, new ChangeUserRoleRequest { Role = AppRoles.Manager });

            await act.Should().ThrowAsync<BadRequestException>().WithMessage("*before changing the role.");
        }

        [Fact]
        public async Task ChangeRoleAsync_WhenOnlyLeadsArchivedProjectsOrRemovedFromTeams_IsAllowed()
        {
            var user = SetupUserForRoleChange("emp-1", AppRoles.Employee);
            _uow.SetupRepo(new List<Project> { new() { ManagerId = user.Id, Status = ProjectStatus.Archived } });
            _uow.SetupRepo(new List<ProjectAssignment> { new() { EmployeeId = user.Id, IsActive = false } });
            _uow.SetupTransaction<bool>();
            _userManager.Setup(m => m.RemoveFromRolesAsync(user, It.IsAny<IEnumerable<string>>())).ReturnsAsync(IdentityResult.Success);
            _userManager.Setup(m => m.AddToRoleAsync(user, AppRoles.Manager)).ReturnsAsync(IdentityResult.Success);

            await _sut.ChangeRoleAsync(AdminId, user.Id, new ChangeUserRoleRequest { Role = AppRoles.Manager });

            _userManager.Verify(m => m.AddToRoleAsync(user, AppRoles.Manager), Times.Once);
        }

        [Fact]
        public async Task ChangeRoleAsync_ReplacesExistingRole()
        {
            var user = NewUser("emp-1");
            _uow.SetupIdentityData((user, AppRoles.Employee));
            _uow.SetupRepo(new List<Project>());
            _uow.SetupRepo(new List<ProjectAssignment>());
            _uow.SetupTransaction<bool>();
            _userManager.Setup(m => m.FindByIdAsync(user.Id)).ReturnsAsync(user);
            _userManager.Setup(m => m.GetRolesAsync(user)).ReturnsAsync(new List<string> { AppRoles.Employee });
            _userManager.Setup(m => m.RemoveFromRolesAsync(user, It.IsAny<IEnumerable<string>>())).ReturnsAsync(IdentityResult.Success);
            _userManager.Setup(m => m.AddToRoleAsync(user, AppRoles.Manager)).ReturnsAsync(IdentityResult.Success);

            await _sut.ChangeRoleAsync(AdminId, user.Id, new ChangeUserRoleRequest { Role = "Manager" });

            _userManager.Verify(m => m.RemoveFromRolesAsync(user, It.Is<IEnumerable<string>>(r => r.Single() == AppRoles.Employee)), Times.Once);
            _userManager.Verify(m => m.AddToRoleAsync(user, AppRoles.Manager), Times.Once);
        }

        // ---------------------------------------------------------------- Block

        [Fact]
        public async Task SetBlockedAsync_BlockingSelf_ThrowsBadRequest()
        {
            var act = () => _sut.SetBlockedAsync(AdminId, AdminId, isBlocked: true);

            await act.Should().ThrowAsync<BadRequestException>().WithMessage("You cannot block your own account.");
        }

        [Fact]
        public async Task SetBlockedAsync_WhenUserMissing_ThrowsNotFound()
        {
            _userManager.Setup(m => m.FindByIdAsync("ghost")).ReturnsAsync((ApplicationUser?)null);

            var act = () => _sut.SetBlockedAsync(AdminId, "ghost", isBlocked: true);

            await act.Should().ThrowAsync<NotFoundException>();
        }

        [Fact]
        public async Task SetBlockedAsync_BlocksUser()
        {
            var user = NewUser("emp-1");
            _uow.SetupIdentityData((user, AppRoles.Employee));
            _userManager.Setup(m => m.FindByIdAsync(user.Id)).ReturnsAsync(user);
            _userManager.Setup(m => m.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);

            var result = await _sut.SetBlockedAsync(AdminId, user.Id, isBlocked: true);

            result.IsBlocked.Should().BeTrue();
            _userManager.Verify(m => m.UpdateAsync(It.Is<ApplicationUser>(u => u.IsBlocked)), Times.Once);
        }

        [Fact]
        public async Task SetBlockedAsync_WhenAlreadyBlocked_DoesNotUpdate()
        {
            var user = NewUser("emp-1", isBlocked: true);
            _uow.SetupIdentityData((user, AppRoles.Employee));
            _userManager.Setup(m => m.FindByIdAsync(user.Id)).ReturnsAsync(user);

            await _sut.SetBlockedAsync(AdminId, user.Id, isBlocked: true);

            _userManager.Verify(m => m.UpdateAsync(It.IsAny<ApplicationUser>()), Times.Never);
        }

        // ---------------------------------------------------------------- Delete

        [Fact]
        public async Task DeleteAsync_OnSelf_ThrowsBadRequest()
        {
            var act = () => _sut.DeleteAsync(AdminId, AdminId);

            await act.Should().ThrowAsync<BadRequestException>().WithMessage("You cannot delete your own account.");
        }

        [Fact]
        public async Task DeleteAsync_WhenUserHasRelatedRecords_ThrowsBadRequest()
        {
            var user = NewUser("emp-1");
            _userManager.Setup(m => m.FindByIdAsync(user.Id)).ReturnsAsync(user);
            _userManager.Setup(m => m.DeleteAsync(user)).ThrowsAsync(new DbUpdateException("FK conflict"));

            var act = () => _sut.DeleteAsync(AdminId, user.Id);

            await act.Should().ThrowAsync<BadRequestException>().WithMessage("*Block the account instead.");
        }

        // ---------------------------------------------------------------- Options (director pickers)

        [Fact]
        public async Task GetOptionsAsync_ReturnsOnlyActiveUsersOfThatRole()
        {
            _uow.SetupIdentityData(
                (NewUser("mgr-1", "Zed"), AppRoles.Manager),
                (NewUser("mgr-2", "Amy"), AppRoles.Manager),
                (NewUser("mgr-3", "Blocked", isBlocked: true), AppRoles.Manager),
                (NewUser("emp-1"), AppRoles.Employee));

            var result = await _sut.GetOptionsAsync("manager");

            result.Select(u => u.Id).Should().Equal("mgr-2", "mgr-1");
        }

        [Fact]
        public async Task GetOptionsAsync_ForAdminRole_ThrowsBadRequest()
        {
            var act = () => _sut.GetOptionsAsync(AppRoles.Admin);

            await act.Should().ThrowAsync<BadRequestException>();
        }

        // ------------------------------------------------------------------

        private ApplicationUser SetupUserForRoleChange(string id, string currentRole)
        {
            var user = NewUser(id);
            _uow.SetupIdentityData((user, currentRole));
            _userManager.Setup(m => m.FindByIdAsync(id)).ReturnsAsync(user);
            _userManager.Setup(m => m.GetRolesAsync(user)).ReturnsAsync(new List<string> { currentRole });
            return user;
        }

        private static CreateUserRequest ValidCreateRequest(string role = AppRoles.Employee) => new()
        {
            FullName = "  Sara Haddad ",
            Email = "sara@test.local",
            UserName = "sara",
            Password = "Pass@1234",
            Role = role
        };
    }
}
