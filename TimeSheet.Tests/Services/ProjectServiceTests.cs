using FluentAssertions;
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
    public class ProjectServiceTests
    {
        private const string DirectorId = "dir-1";
        private const string OtherDirectorId = "dir-2";
        private const string ManagerId = "mgr-1";
        private const string OtherManagerId = "mgr-2";
        private const string BlockedManagerId = "mgr-blocked";
        private const string Employee1 = "emp-1";
        private const string Employee2 = "emp-2";
        private const string BlockedEmployee = "emp-blocked";

        private readonly Mock<IUnitOfWork> _uow = new();
        private readonly Dictionary<string, ApplicationUser> _users;
        private readonly Client _acme = new() { Id = Guid.NewGuid(), Name = "Acme", IsActive = true };
        private readonly Client _inactiveClient = new() { Id = Guid.NewGuid(), Name = "Old Co", IsActive = false };
        private readonly List<Project> _projects = new();
        private readonly List<ProjectAssignment> _assignments = new();
        private readonly List<TimeEntry> _entries = new();
        private readonly Mock<IGenericRepository<ProjectAssignment>> _assignmentRepo;
        private readonly ProjectService _sut;

        public ProjectServiceTests()
        {
            var identity = _uow.SetupIdentityData(
                (NewUser(DirectorId, "Dina Director"), AppRoles.Director),
                (NewUser(OtherDirectorId, "Other Director"), AppRoles.Director),
                (NewUser(ManagerId, "Mona Manager"), AppRoles.Manager),
                (NewUser(OtherManagerId, "Omar Manager"), AppRoles.Manager),
                (NewUser(BlockedManagerId, "Blocked Manager", isBlocked: true), AppRoles.Manager),
                (NewUser(Employee1, "Eve Employee"), AppRoles.Employee),
                (NewUser(Employee2, "Adam Employee"), AppRoles.Employee),
                (NewUser(BlockedEmployee, "Blocked Employee", isBlocked: true), AppRoles.Employee));
            _users = identity.Users.ToDictionary(u => u.Id);

            _uow.SetupRepo(new List<Client> { _acme, _inactiveClient });
            _uow.SetupRepo(_entries);

            // Adding a project/assignment wires up navigations the way EF would.
            var projectRepo = _uow.SetupRepo(_projects);
            projectRepo.Setup(r => r.AddAsync(It.IsAny<Project>(), It.IsAny<CancellationToken>()))
                .Callback<Project, CancellationToken>((p, _) =>
                {
                    p.Id = Guid.NewGuid();
                    p.Client = _acme;
                    p.Director = _users[p.DirectorId];
                    p.Manager = _users[p.ManagerId];
                    _projects.Add(p);
                    foreach (var a in p.Assignments)
                        Wire(a, p);
                });

            _assignmentRepo = _uow.SetupRepo(_assignments);
            _assignmentRepo.Setup(r => r.AddAsync(It.IsAny<ProjectAssignment>(), It.IsAny<CancellationToken>()))
                .Callback<ProjectAssignment, CancellationToken>((a, _) =>
                {
                    var project = _projects.Single(p => p.Id == a.ProjectId);
                    project.Assignments.Add(a);
                    Wire(a, project);
                });

            _uow.SetupTransaction<Guid>();

            _sut = new ProjectService(_uow.Object, new ProjectAccessService(_uow.Object));
        }

        // ---------------------------------------------------------------- Create

        [Fact]
        public async Task CreateAsync_FirstProject_GetsCodePRJ0001_AndIsOwnedByDirector()
        {
            var result = await _sut.CreateAsync(DirectorId, ValidCreate(Employee1, Employee2));

            result.Code.Should().Be("PRJ-0001");
            result.DirectorId.Should().Be(DirectorId);
            result.ManagerName.Should().Be("Mona Manager");
            result.Status.Should().Be(ProjectStatus.Active);
            result.ActiveEmployeeCount.Should().Be(2);
            result.Employees.Select(e => e.FullName).Should().Equal("Adam Employee", "Eve Employee");
        }

        [Theory]
        [InlineData(new[] { "PRJ-0009", "PRJ-0010" }, "PRJ-0011")]
        [InlineData(new[] { "PRJ-9999" }, "PRJ-10000")]
        [InlineData(new[] { "PRJ-9999", "PRJ-10000" }, "PRJ-10001")] // longest code wins, not string order
        public async Task CreateAsync_ContinuesFromHighestCode(string[] existing, string expected)
        {
            foreach (var code in existing)
                AddProject(code);

            var result = await _sut.CreateAsync(DirectorId, ValidCreate());

            result.Code.Should().Be(expected);
        }

        [Fact]
        public async Task CreateAsync_RunsInSerializableTransaction()
        {
            await _sut.CreateAsync(DirectorId, ValidCreate());

            _uow.Verify(u => u.ExecuteInTransactionAsync(
                It.IsAny<Func<Task<Guid>>>(), System.Data.IsolationLevel.Serializable, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Theory]
        [InlineData(Employee1)]        // not a Manager
        [InlineData(BlockedManagerId)] // blocked
        [InlineData("ghost")]          // doesn't exist
        public async Task CreateAsync_WithInvalidManager_ThrowsBadRequest(string managerId)
        {
            var request = ValidCreate();
            request.ManagerId = managerId;

            var act = () => _sut.CreateAsync(DirectorId, request);

            await act.Should().ThrowAsync<BadRequestException>().WithMessage("*Manager role*");
            _projects.Should().BeEmpty();
        }

        [Fact]
        public async Task CreateAsync_ManagerIsTheDirector_ThrowsBadRequest()
        {
            var request = ValidCreate();
            request.ManagerId = DirectorId;

            var act = () => _sut.CreateAsync(DirectorId, request);

            await act.Should().ThrowAsync<BadRequestException>().WithMessage("The manager cannot be the project's director.");
        }

        [Fact]
        public async Task CreateAsync_WithInactiveClient_ThrowsBadRequest()
        {
            var request = ValidCreate();
            request.ClientId = _inactiveClient.Id;

            var act = () => _sut.CreateAsync(DirectorId, request);

            await act.Should().ThrowAsync<BadRequestException>().WithMessage("Client not found or inactive.");
        }

        [Fact]
        public async Task CreateAsync_EndDateBeforeStartDate_ThrowsBadRequest()
        {
            var request = ValidCreate();
            request.EndDate = request.StartDate!.Value.AddDays(-1);

            var act = () => _sut.CreateAsync(DirectorId, request);

            await act.Should().ThrowAsync<BadRequestException>().WithMessage("End date cannot be before the start date.");
        }

        [Theory]
        [InlineData(OtherManagerId)]   // managers never log hours
        [InlineData(BlockedEmployee)]
        public async Task CreateAsync_AssigningNonActiveEmployee_ThrowsBadRequest(string userId)
        {
            var act = () => _sut.CreateAsync(DirectorId, ValidCreate(Employee1, userId));

            await act.Should().ThrowAsync<BadRequestException>().WithMessage($"*Employee role*Invalid: {userId}.");
            _projects.Should().BeEmpty();
        }

        // ---------------------------------------------------------------- Update / budget lock

        [Fact]
        public async Task UpdateAsync_ChangingBudget_WhenProjectHasApprovedEntries_ThrowsBadRequest()
        {
            var project = AddProject("PRJ-0001", budget: 100);
            AddEntry(project, TimeEntryStatus.Approved);

            var act = () => _sut.UpdateAsync(DirectorId, project.Id, UpdateFrom(project, budget: 150));

            await act.Should().ThrowAsync<BadRequestException>().WithMessage("The budget is locked*");
            project.BudgetHours.Should().Be(100);
            _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task UpdateAsync_ChangingBudget_WithOnlyPendingOrRejectedEntries_Succeeds()
        {
            var project = AddProject("PRJ-0001", budget: 100);
            AddEntry(project, TimeEntryStatus.PendingManager);
            AddEntry(project, TimeEntryStatus.PendingDirector);
            AddEntry(project, TimeEntryStatus.Rejected);

            var result = await _sut.UpdateAsync(DirectorId, project.Id, UpdateFrom(project, budget: 150));

            result.BudgetHours.Should().Be(150);
            result.IsBudgetLocked.Should().BeFalse();
        }

        [Fact]
        public async Task UpdateAsync_OtherFields_WhenBudgetIsLocked_Succeeds()
        {
            var project = AddProject("PRJ-0001", budget: 100);
            AddEntry(project, TimeEntryStatus.Approved);

            var request = UpdateFrom(project, budget: 100);
            request.Name = "Renamed";
            request.ManagerId = OtherManagerId;

            var result = await _sut.UpdateAsync(DirectorId, project.Id, request);

            result.Name.Should().Be("Renamed");
            result.ManagerId.Should().Be(OtherManagerId);
            result.IsBudgetLocked.Should().BeTrue();
        }

        [Fact]
        public async Task UpdateAsync_AnotherDirectorsProject_ThrowsNotFound()
        {
            var project = AddProject("PRJ-0001", directorId: OtherDirectorId);

            var act = () => _sut.UpdateAsync(DirectorId, project.Id, UpdateFrom(project, budget: 100));

            await act.Should().ThrowAsync<NotFoundException>().WithMessage("Project not found.");
        }

        // ---------------------------------------------------------------- Reads / scoping

        [Fact]
        public async Task GetAllAsync_Director_SeesOnlyProjectsHeDirects()
        {
            AddProject("PRJ-0001");
            AddProject("PRJ-0002", directorId: OtherDirectorId);
            AddProject("PRJ-0003");

            var result = await _sut.GetAllAsync(DirectorId, AppRoles.Director, new ProjectFilterRequest());

            result.Select(p => p.Code).Should().Equal("PRJ-0001", "PRJ-0003");
        }

        [Fact]
        public async Task GetAllAsync_Manager_SeesOnlyProjectsHeManages()
        {
            AddProject("PRJ-0001", managerId: ManagerId);
            AddProject("PRJ-0002", managerId: OtherManagerId);

            var result = await _sut.GetAllAsync(OtherManagerId, AppRoles.Manager, new ProjectFilterRequest());

            result.Select(p => p.Code).Should().Equal("PRJ-0002");
        }

        [Fact]
        public async Task GetByIdAsync_ManagerOfAnotherProject_ThrowsNotFound()
        {
            var project = AddProject("PRJ-0001", managerId: ManagerId);

            var act = () => _sut.GetByIdAsync(OtherManagerId, AppRoles.Manager, project.Id);

            await act.Should().ThrowAsync<NotFoundException>();
        }

        // ---------------------------------------------------------------- Status

        [Fact]
        public async Task ChangeStatusAsync_UpdatesStatus()
        {
            var project = AddProject("PRJ-0001");

            var result = await _sut.ChangeStatusAsync(DirectorId, project.Id,
                new ChangeProjectStatusRequest { Status = ProjectStatus.OnHold });

            result.Status.Should().Be(ProjectStatus.OnHold);
            _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        // ---------------------------------------------------------------- Assignments

        [Fact]
        public async Task AssignEmployeesAsync_AddsNew_ReactivatesRemoved_IgnoresActive()
        {
            var project = AddProject("PRJ-0001", employeeIds: new[] { Employee1 });
            var removed = AddAssignment(project, Employee2, isActive: false);

            var result = await _sut.AssignEmployeesAsync(DirectorId, project.Id,
                new AssignEmployeesRequest { EmployeeIds = new() { Employee1, Employee2 } });

            removed.IsActive.Should().BeTrue();
            project.Assignments.Should().HaveCount(2); // no duplicate row for Employee1
            result.ActiveEmployeeCount.Should().Be(2);
            _assignmentRepo.Verify(r => r.AddAsync(It.IsAny<ProjectAssignment>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task AssignEmployeesAsync_NewEmployee_IsAdded()
        {
            var project = AddProject("PRJ-0001");

            var result = await _sut.AssignEmployeesAsync(DirectorId, project.Id,
                new AssignEmployeesRequest { EmployeeIds = new() { Employee1 } });

            result.Employees.Should().ContainSingle(e => e.EmployeeId == Employee1 && e.IsActive);
        }

        [Fact]
        public async Task AssignEmployeesAsync_ManagerUser_ThrowsBadRequest()
        {
            var project = AddProject("PRJ-0001");

            var act = () => _sut.AssignEmployeesAsync(DirectorId, project.Id,
                new AssignEmployeesRequest { EmployeeIds = new() { ManagerId } });

            await act.Should().ThrowAsync<BadRequestException>().WithMessage("*Employee role*");
        }

        [Fact]
        public async Task AssignEmployeesAsync_AnotherDirectorsProject_ThrowsNotFound()
        {
            var project = AddProject("PRJ-0001", directorId: OtherDirectorId);

            var act = () => _sut.AssignEmployeesAsync(DirectorId, project.Id,
                new AssignEmployeesRequest { EmployeeIds = new() { Employee1 } });

            await act.Should().ThrowAsync<NotFoundException>();
        }

        [Fact]
        public async Task RemoveEmployeeAsync_DeactivatesAssignment_KeepsHistory()
        {
            var project = AddProject("PRJ-0001", employeeIds: new[] { Employee1 });

            var result = await _sut.RemoveEmployeeAsync(DirectorId, project.Id, Employee1);

            _assignments.Single().IsActive.Should().BeFalse();
            result.ActiveEmployeeCount.Should().Be(0);
            result.Employees.Should().ContainSingle(e => e.EmployeeId == Employee1 && !e.IsActive);
        }

        [Fact]
        public async Task RemoveEmployeeAsync_WhenNotAssigned_ThrowsNotFound()
        {
            var project = AddProject("PRJ-0001");

            var act = () => _sut.RemoveEmployeeAsync(DirectorId, project.Id, Employee1);

            await act.Should().ThrowAsync<NotFoundException>().WithMessage("This employee is not assigned to the project.");
        }

        // ------------------------------------------------------------------

        private CreateProjectRequest ValidCreate(params string[] employeeIds) => new()
        {
            Name = "ERP Rollout",
            ClientId = _acme.Id,
            ManagerId = ManagerId,
            BudgetHours = 100,
            StartDate = new DateOnly(2026, 10, 1),
            EmployeeIds = employeeIds.ToList()
        };

        private static UpdateProjectRequest UpdateFrom(Project p, decimal budget) => new()
        {
            Name = p.Name,
            ClientId = p.ClientId,
            ManagerId = p.ManagerId,
            BudgetHours = budget,
            StartDate = p.StartDate,
            EndDate = p.EndDate
        };

        private Project AddProject(
            string code,
            string directorId = DirectorId,
            string managerId = ManagerId,
            decimal budget = 100,
            params string[] employeeIds)
        {
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Code = code,
                Name = "Project " + code,
                ClientId = _acme.Id,
                Client = _acme,
                DirectorId = directorId,
                Director = _users[directorId],
                ManagerId = managerId,
                Manager = _users[managerId],
                BudgetHours = budget,
                StartDate = new DateOnly(2026, 9, 1),
                Status = ProjectStatus.Active
            };
            _projects.Add(project);

            foreach (var employeeId in employeeIds)
                AddAssignment(project, employeeId, isActive: true);

            return project;
        }

        private ProjectAssignment AddAssignment(Project project, string employeeId, bool isActive)
        {
            var assignment = new ProjectAssignment { EmployeeId = employeeId, IsActive = isActive };
            project.Assignments.Add(assignment);
            Wire(assignment, project);
            return assignment;
        }

        private void Wire(ProjectAssignment assignment, Project project)
        {
            assignment.Id = assignment.Id == Guid.Empty ? Guid.NewGuid() : assignment.Id;
            assignment.Project = project;
            assignment.ProjectId = project.Id;
            assignment.Employee = _users[assignment.EmployeeId];
            if (!_assignments.Contains(assignment))
                _assignments.Add(assignment);
        }

        private void AddEntry(Project project, TimeEntryStatus status)
        {
            _entries.Add(new TimeEntry
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                EmployeeId = Employee1,
                WorkDate = new DateOnly(2026, 10, 1),
                Hours = 4,
                Status = status
            });
        }
    }
}
