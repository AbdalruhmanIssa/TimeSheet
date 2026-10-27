using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using TimeSheet.BLL.DTOs.Request;
using TimeSheet.BLL.Exceptions;
using TimeSheet.BLL.Services.Classes;
using TimeSheet.BLL.Settings;
using TimeSheet.DAL.Enums;
using TimeSheet.DAL.Models;
using TimeSheet.DAL.Repositories.Interface;
using TimeSheet.DAL.Utilities;
using static TimeSheet.Tests.TestHelpers;

namespace TimeSheet.Tests.Services
{
    public class ReportServiceTests
    {
        private const string Director = "dir-1";
        private const string OtherDirector = "dir-2";
        private const string Manager = "mgr-1";
        private const string OtherManager = "mgr-2";

        // Business "today" (Asia/Riyadh) = 2026-10-27.
        private static readonly DateTimeOffset UtcNow = new(2026, 10, 26, 22, 30, 0, TimeSpan.Zero);

        private readonly Mock<IUnitOfWork> _uow = new();
        private readonly Dictionary<string, ApplicationUser> _users;
        private readonly Client _client = new() { Id = Guid.NewGuid(), Name = "Acme" };
        private readonly List<Project> _projects = new();
        private readonly List<ProjectAssignment> _assignments = new();
        private readonly List<TimeEntry> _entries = new();

        private readonly Project _project; // dir-1 / mgr-1, budget 100, starts 2026-10-01
        private readonly ReportService _sut;

        public ReportServiceTests()
        {
            _users = new[]
            {
                NewUser(Director, "Dina Director"), NewUser(OtherDirector, "Other Director"),
                NewUser(Manager, "Mona Manager"), NewUser(OtherManager, "Omar Manager"),
                NewUser("emp-1", "Eve"), NewUser("emp-2", "Adam"), NewUser("emp-3", "Zoe"),
                NewUser("emp-4", "Bob"), NewUser("emp-5", "Carl")
            }.ToDictionary(u => u.Id);

            _project = AddProject("PRJ-0001", budget: 100);

            _uow.SetupRepo(_projects);
            _uow.SetupRepo(_assignments);
            _uow.SetupRepo(_entries);
            _uow.SetupRepo(_users.Values.ToList());

            var settings = Options.Create(new TimeSheetSettings
            {
                BusinessTimeZone = "Asia/Riyadh",
                WeekStart = DayOfWeek.Sunday,
                MaxDailyHours = 12,
                MaxBackdateDays = 30
            });
            var clock = new BusinessClock(new FixedTimeProvider(UtcNow), settings);

            _sut = new ReportService(_uow.Object, new ProjectAccessService(_uow.Object), clock);
        }

        // ---------------------------------------------------------------- Summary

        [Fact]
        public async Task GetProjectSummaryAsync_SplitsHoursByStatus_AndIgnoresRejectedAndCancelled()
        {
            Add(_project, "emp-1", "2026-10-02", 30, TimeEntryStatus.Approved);
            Add(_project, "emp-2", "2026-10-02", 20, TimeEntryStatus.Approved);
            Add(_project, "emp-1", "2026-10-03", 8, TimeEntryStatus.PendingManager);
            Add(_project, "emp-2", "2026-10-03", 4, TimeEntryStatus.PendingDirector);
            Add(_project, "emp-1", "2026-10-04", 7, TimeEntryStatus.Rejected);
            Add(_project, "emp-2", "2026-10-04", 6, TimeEntryStatus.Cancelled);
            Add(AddProject("PRJ-0002", budget: 10), "emp-1", "2026-10-02", 9, TimeEntryStatus.Approved);

            var report = await _sut.GetProjectSummaryAsync(Director, AppRoles.Director, _project.Id);

            report.Code.Should().Be("PRJ-0001");
            report.ClientName.Should().Be("Acme");
            report.ManagerName.Should().Be("Mona Manager");
            report.ActualHours.Should().Be(50);
            report.PendingManagerHours.Should().Be(8);
            report.PendingDirectorHours.Should().Be(4);
            report.RemainingHours.Should().Be(50);
            report.ProjectedRemaining.Should().Be(38);
            report.UsagePercent.Should().Be(50);
            report.BudgetStatus.Should().Be(BudgetStatus.OnTrack);
        }

        [Fact]
        public async Task GetProjectSummaryAsync_ManagerOfTheProject_CanSeeIt()
        {
            var report = await _sut.GetProjectSummaryAsync(Manager, AppRoles.Manager, _project.Id);

            report.ActualHours.Should().Be(0);
        }

        [Theory]
        [InlineData(OtherDirector, AppRoles.Director)]
        [InlineData(OtherManager, AppRoles.Manager)]
        [InlineData("emp-1", AppRoles.Employee)]
        public async Task GetProjectSummaryAsync_OutOfScope_ThrowsNotFound(string userId, string role)
        {
            var act = () => _sut.GetProjectSummaryAsync(userId, role, _project.Id);

            await act.Should().ThrowAsync<NotFoundException>().WithMessage("Project not found.");
        }

        // ---------------------------------------------------------------- Portfolio

        [Fact]
        public async Task GetPortfolioAsync_ListsOnlyMyProjects_ProblemsFirst()
        {
            // _project (PRJ-0001): 10 / 100 -> OnTrack
            Add(_project, "emp-1", "2026-10-02", 10, TimeEntryStatus.Approved);

            var exceeded = AddProject("PRJ-0002", budget: 100);          // 120 / 100 -> Exceeded
            Add(exceeded, "emp-1", "2026-10-02", 12, TimeEntryStatus.Approved, count: 10);

            var projected = AddProject("PRJ-0003", budget: 100);         // 50 approved + 60 pending -> projected over
            Add(projected, "emp-1", "2026-10-02", 10, TimeEntryStatus.Approved, count: 5);
            Add(projected, "emp-2", "2026-10-02", 12, TimeEntryStatus.PendingManager, count: 5);

            var nearLimit = AddProject("PRJ-0004", budget: 100);         // 85 / 100 -> NearLimit
            Add(nearLimit, "emp-1", "2026-10-02", 8.5m, TimeEntryStatus.Approved, count: 10);

            AddProject("PRJ-0005", budget: 100, directorId: OtherDirector); // not mine

            var result = (await _sut.GetPortfolioAsync(Director, new PortfolioFilterRequest())).Items;

            result.Select(r => r.Code).Should().Equal("PRJ-0002", "PRJ-0003", "PRJ-0004", "PRJ-0001");
            result[0].BudgetStatus.Should().Be(BudgetStatus.Exceeded);
            result[0].ExceededBy.Should().Be(20);
            result[1].IsProjectedToExceed.Should().BeTrue();
            result[2].BudgetStatus.Should().Be(BudgetStatus.NearLimit);
        }

        [Fact]
        public async Task GetPortfolioAsync_FiltersByProjectStatus()
        {
            AddProject("PRJ-0002", budget: 50).Status = ProjectStatus.Archived;

            var result = (await _sut.GetPortfolioAsync(Director, new PortfolioFilterRequest { Status = ProjectStatus.Archived })).Items;

            result.Select(r => r.Code).Should().Equal("PRJ-0002");
        }

        // ---------------------------------------------------------------- Daily

        [Fact]
        public async Task GetDailyReportAsync_ByDay_OpeningBalanceThenOnlyDaysWithApprovedHours()
        {
            Add(_project, "emp-1", "2026-10-01", 5, TimeEntryStatus.Approved);   // before range
            Add(_project, "emp-2", "2026-10-02", 3, TimeEntryStatus.Approved);   // before range
            Add(_project, "emp-1", "2026-10-03", 2, TimeEntryStatus.Approved);
            Add(_project, "emp-1", "2026-10-04", 6, TimeEntryStatus.PendingDirector); // not approved
            Add(_project, "emp-1", "2026-10-05", 4, TimeEntryStatus.Approved);
            Add(_project, "emp-2", "2026-10-05", 7, TimeEntryStatus.Rejected);   // not approved

            var report = await _sut.GetDailyReportAsync(Director, AppRoles.Director, _project.Id,
                Filter("2026-10-03", "2026-10-06"));

            report.OpeningBalance.Should().Be(8);
            // 10-04 (only pending) and 10-06 (nothing) are left out.
            report.Rows.Select(r => r.PeriodStart).Should().Equal(D("2026-10-03"), D("2026-10-05"));
            report.Rows.Select(r => r.Hours).Should().Equal(2m, 4m);
            report.Rows.Select(r => r.RunningTotal).Should().Equal(10m, 14m);
            report.Rows.Select(r => r.Remaining).Should().Equal(90m, 86m);
            report.PeriodHours.Should().Be(6);
            report.ClosingBalance.Should().Be(14);
        }

        [Fact]
        public async Task GetDailyReportAsync_NoDates_RunsFromProjectStartToBusinessToday()
        {
            Add(_project, "emp-1", "2026-10-27", 3, TimeEntryStatus.Approved); // today in Riyadh

            var report = await _sut.GetDailyReportAsync(Director, AppRoles.Director, _project.Id, new ReportFilterRequest());

            report.From.Should().Be(D("2026-10-01"));
            report.To.Should().Be(D("2026-10-27")); // today in Riyadh, although UTC is still the 26th
            report.Rows.Select(r => r.PeriodStart).Should().Equal(D("2026-10-27"));
            report.OpeningBalance.Should().Be(0);
        }

        [Fact]
        public async Task GetDailyReportAsync_NoApprovedHoursInRange_HasNoRows_ButKeepsTheBalances()
        {
            Add(_project, "emp-1", "2026-10-01", 5, TimeEntryStatus.Approved);        // before range
            Add(_project, "emp-1", "2026-10-10", 4, TimeEntryStatus.PendingManager);  // not approved

            var report = await _sut.GetDailyReportAsync(Director, AppRoles.Director, _project.Id,
                Filter("2026-10-05", "2026-10-20"));

            report.Rows.Should().BeEmpty();
            report.OpeningBalance.Should().Be(5);
            report.PeriodHours.Should().Be(0);
            report.ClosingBalance.Should().Be(5);
        }

        [Fact]
        public async Task GetDailyReportAsync_ByWeek_WeeksStartOnSunday_EdgesClampedToRange()
        {
            Add(_project, "emp-1", "2026-10-02", 4, TimeEntryStatus.Approved); // Fri, week 1
            Add(_project, "emp-1", "2026-10-04", 3, TimeEntryStatus.Approved); // Sun, week 2
            Add(_project, "emp-1", "2026-10-10", 2, TimeEntryStatus.Approved); // Sat, week 2
            Add(_project, "emp-1", "2026-10-19", 1, TimeEntryStatus.Approved); // Mon, week 4

            // 2026-10-01 is a Thursday, 2026-10-20 a Tuesday.
            var report = await _sut.GetDailyReportAsync(Director, AppRoles.Director, _project.Id,
                Filter("2026-10-01", "2026-10-20", ReportGroupBy.Week));

            // The week of 10-11 has no hours, so it is left out.
            report.Rows.Select(r => (r.PeriodStart, r.PeriodEnd)).Should().Equal(
                (D("2026-10-01"), D("2026-10-03")),
                (D("2026-10-04"), D("2026-10-10")),
                (D("2026-10-18"), D("2026-10-20")));
            report.Rows.Select(r => r.Hours).Should().Equal(4m, 5m, 1m);
            report.Rows.Last().RunningTotal.Should().Be(10);
        }

        [Fact]
        public async Task GetDailyReportAsync_ByMonth_PartialFirstAndLastMonths()
        {
            Add(_project, "emp-1", "2026-09-20", 4, TimeEntryStatus.Approved);
            Add(_project, "emp-1", "2026-10-15", 6, TimeEntryStatus.Approved);
            Add(_project, "emp-1", "2026-11-05", 1, TimeEntryStatus.Approved);

            var report = await _sut.GetDailyReportAsync(Director, AppRoles.Director, _project.Id,
                Filter("2026-09-15", "2026-11-10", ReportGroupBy.Month));

            report.Rows.Select(r => (r.PeriodStart, r.PeriodEnd)).Should().Equal(
                (D("2026-09-15"), D("2026-09-30")),
                (D("2026-10-01"), D("2026-10-31")),
                (D("2026-11-01"), D("2026-11-10")));
            report.Rows.Select(r => r.Hours).Should().Equal(4m, 6m, 1m);
        }

        [Fact]
        public async Task GetDailyReportAsync_FromAfterTo_ThrowsBadRequest()
        {
            var act = () => _sut.GetDailyReportAsync(Director, AppRoles.Director, _project.Id, Filter("2026-10-10", "2026-10-01"));

            await act.Should().ThrowAsync<BadRequestException>().WithMessage("'from' cannot be after 'to'.");
        }

        [Fact]
        public async Task GetDailyReportAsync_ByDay_RangeTooLong_ThrowsBadRequest_ButByWeekIsFine()
        {
            var tooLong = Filter("2025-01-01", "2026-01-02"); // 367 days
            Add(_project, "emp-1", "2025-06-01", 2, TimeEntryStatus.Approved);

            var act = () => _sut.GetDailyReportAsync(Director, AppRoles.Director, _project.Id, tooLong);
            await act.Should().ThrowAsync<BadRequestException>().WithMessage("*Use groupBy=week or month*");

            tooLong.GroupBy = ReportGroupBy.Week;
            var report = await _sut.GetDailyReportAsync(Director, AppRoles.Director, _project.Id, tooLong);
            report.Rows.Should().ContainSingle(r => r.Hours == 2);
        }

        [Fact]
        public async Task GetDailyReportAsync_OtherManager_ThrowsNotFound()
        {
            var act = () => _sut.GetDailyReportAsync(OtherManager, AppRoles.Manager, _project.Id, new ReportFilterRequest());

            await act.Should().ThrowAsync<NotFoundException>();
        }

        // ---------------------------------------------------------------- Per employee

        [Fact]
        public async Task GetEmployeeBreakdownAsync_HoursSharesPending_AndWholeTeam()
        {
            Assign("emp-1", active: true);
            Assign("emp-2", active: true);
            Assign("emp-3", active: true);   // on the team, no hours yet
            Assign("emp-4", active: false);  // removed, no hours -> not listed
            Assign("emp-5", active: false);  // removed, has hours -> listed

            Add(_project, "emp-1", "2026-10-02", 10, TimeEntryStatus.Approved, count: 3); // 30
            Add(_project, "emp-2", "2026-10-02", 10, TimeEntryStatus.Approved);           // 10
            Add(_project, "emp-2", "2026-10-03", 5, TimeEntryStatus.PendingManager);      // pending 5
            Add(_project, "emp-5", "2026-10-02", 10, TimeEntryStatus.Approved);           // 10
            Add(_project, "emp-1", "2026-10-09", 8, TimeEntryStatus.Rejected);            // ignored

            var rows = await _sut.GetEmployeeBreakdownAsync(Director, AppRoles.Director, _project.Id, new ReportFilterRequest());

            rows.Select(r => r.EmployeeName).Should().Equal("Eve", "Adam", "Carl", "Zoe");
            rows.Select(r => r.ApprovedHours).Should().Equal(30m, 10m, 10m, 0m);
            rows.Select(r => r.SharePercent).Should().Equal(60m, 20m, 20m, 0m);
            rows.Select(r => r.PendingHours).Should().Equal(0m, 5m, 0m, 0m);
            rows.Single(r => r.EmployeeName == "Carl").IsActiveOnProject.Should().BeFalse();
        }

        [Fact]
        public async Task GetEmployeeBreakdownAsync_RespectsDateRange()
        {
            Assign("emp-1", active: true);
            Add(_project, "emp-1", "2026-10-01", 4, TimeEntryStatus.Approved);
            Add(_project, "emp-1", "2026-10-15", 6, TimeEntryStatus.Approved);

            var rows = await _sut.GetEmployeeBreakdownAsync(Director, AppRoles.Director, _project.Id,
                Filter("2026-10-10", "2026-10-31"));

            rows.Single().ApprovedHours.Should().Be(6);
            rows.Single().SharePercent.Should().Be(100);
        }

        [Fact]
        public async Task GetEmployeeBreakdownAsync_NothingApproved_SharesAreZero()
        {
            Assign("emp-1", active: true);
            Add(_project, "emp-1", "2026-10-02", 4, TimeEntryStatus.PendingDirector);

            var rows = await _sut.GetEmployeeBreakdownAsync(Manager, AppRoles.Manager, _project.Id, new ReportFilterRequest());

            rows.Single().SharePercent.Should().Be(0);
            rows.Single().PendingHours.Should().Be(4);
        }

        [Fact]
        public async Task GetEmployeeBreakdownAsync_OtherDirector_ThrowsNotFound()
        {
            var act = () => _sut.GetEmployeeBreakdownAsync(OtherDirector, AppRoles.Director, _project.Id, new ReportFilterRequest());

            await act.Should().ThrowAsync<NotFoundException>();
        }

        // ------------------------------------------------------------------

        private static DateOnly D(string date) => DateOnly.Parse(date);

        private static ReportFilterRequest Filter(string from, string to, ReportGroupBy groupBy = ReportGroupBy.Day)
            => new() { From = D(from), To = D(to), GroupBy = groupBy };

        private Project AddProject(string code, decimal budget, string directorId = Director, string managerId = Manager)
        {
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Code = code,
                Name = "Project " + code,
                ClientId = _client.Id,
                Client = _client,
                DirectorId = directorId,
                Director = _users[directorId],
                ManagerId = managerId,
                Manager = _users[managerId],
                BudgetHours = budget,
                StartDate = new DateOnly(2026, 10, 1),
                Status = ProjectStatus.Active
            };
            _projects.Add(project);
            return project;
        }

        private void Assign(string employeeId, bool active)
        {
            var assignment = new ProjectAssignment
            {
                Id = Guid.NewGuid(),
                ProjectId = _project.Id,
                EmployeeId = employeeId,
                IsActive = active
            };
            _project.Assignments.Add(assignment);
            _assignments.Add(assignment);
        }

        // `count` entries on consecutive days starting at `date`.
        private void Add(Project project, string employeeId, string date, decimal hours, TimeEntryStatus status, int count = 1)
        {
            for (var i = 0; i < count; i++)
            {
                _entries.Add(new TimeEntry
                {
                    Id = Guid.NewGuid(),
                    ProjectId = project.Id,
                    Project = project,
                    EmployeeId = employeeId,
                    WorkDate = D(date).AddDays(i),
                    Hours = hours,
                    Status = status
                });
            }
        }
    }
}
