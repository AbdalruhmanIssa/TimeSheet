using FluentAssertions;
using Moq;
using TimeSheet.BLL.Exceptions;
using TimeSheet.BLL.Services.Classes;
using TimeSheet.DAL.Models;
using TimeSheet.DAL.Repositories.Interface;
using TimeSheet.DAL.Utilities;

namespace TimeSheet.Tests.Services
{
    // DESIGN §3 rule 11: the ONE visibility rule every service goes through.
    public class ProjectAccessServiceTests
    {
        private readonly Mock<IUnitOfWork> _uow = new();
        private readonly List<Project> _projects = new();
        private readonly ProjectAccessService _sut;

        private readonly Project _p1; // dir-1 / mgr-1, emp-1 active, emp-2 removed
        private readonly Project _p2; // dir-1 / mgr-2, emp-2 active
        private readonly Project _p3; // dir-2 / mgr-1, nobody assigned

        public ProjectAccessServiceTests()
        {
            _p1 = Add("PRJ-0001", "dir-1", "mgr-1", ("emp-1", true), ("emp-2", false));
            _p2 = Add("PRJ-0002", "dir-1", "mgr-2", ("emp-2", true));
            _p3 = Add("PRJ-0003", "dir-2", "mgr-1");
            _uow.SetupRepo(_projects);
            _sut = new ProjectAccessService(_uow.Object);
        }

        [Theory]
        [InlineData("dir-1", AppRoles.Director, new[] { "PRJ-0001", "PRJ-0002" })]
        [InlineData("dir-2", AppRoles.Director, new[] { "PRJ-0003" })]
        [InlineData("mgr-1", AppRoles.Manager, new[] { "PRJ-0001", "PRJ-0003" })]
        [InlineData("mgr-2", AppRoles.Manager, new[] { "PRJ-0002" })]
        [InlineData("emp-1", AppRoles.Employee, new[] { "PRJ-0001" })]
        [InlineData("emp-2", AppRoles.Employee, new[] { "PRJ-0002" })]   // removed from PRJ-0001
        [InlineData("emp-3", AppRoles.Employee, new string[0])]
        [InlineData("dir-1", AppRoles.Admin, new string[0])]            // admins don't see projects
        [InlineData("dir-1", "SomethingElse", new string[0])]
        public void VisibleProjects_PerRole(string userId, string role, string[] expectedCodes)
        {
            _sut.VisibleProjects(userId, role).Select(p => p.Code).OrderBy(c => c)
                .Should().Equal(expectedCodes);
        }

        [Fact]
        public void VisibleProjects_RoleIsWhatMatters_NotTheUserIdAlone()
        {
            // mgr-1 is the manager of PRJ-0001, but asking "as Director" shows nothing.
            _sut.VisibleProjects("mgr-1", AppRoles.Director).Should().BeEmpty();
        }

        [Fact]
        public async Task GetVisibleProjectAsync_InScope_ReturnsTheProject()
        {
            var project = await _sut.GetVisibleProjectAsync(_p2.Id, "mgr-2", AppRoles.Manager);

            project.Should().BeSameAs(_p2);
        }

        [Theory]
        [InlineData("dir-2", AppRoles.Director)]
        [InlineData("mgr-2", AppRoles.Manager)]
        [InlineData("emp-2", AppRoles.Employee)]
        public async Task GetVisibleProjectAsync_OutOfScope_ThrowsNotFound(string userId, string role)
        {
            var act = () => _sut.GetVisibleProjectAsync(_p1.Id, userId, role);

            await act.Should().ThrowAsync<NotFoundException>().WithMessage("Project not found.");
        }

        [Fact]
        public async Task GetVisibleProjectAsync_UnknownId_ThrowsNotFound()
        {
            var act = () => _sut.GetVisibleProjectAsync(Guid.NewGuid(), "dir-1", AppRoles.Director);

            await act.Should().ThrowAsync<NotFoundException>();
        }

        private Project Add(string code, string directorId, string managerId, params (string EmployeeId, bool Active)[] team)
        {
            var project = new Project { Id = Guid.NewGuid(), Code = code, DirectorId = directorId, ManagerId = managerId };
            foreach (var (employeeId, active) in team)
                project.Assignments.Add(new ProjectAssignment { ProjectId = project.Id, EmployeeId = employeeId, IsActive = active });
            _projects.Add(project);
            return project;
        }
    }
}
