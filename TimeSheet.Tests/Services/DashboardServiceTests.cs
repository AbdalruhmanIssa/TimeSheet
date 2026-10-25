using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using TimeSheet.BLL.DTOs.Response;
using TimeSheet.BLL.Services.Classes;
using TimeSheet.BLL.Services.Interface;
using TimeSheet.BLL.Settings;
using TimeSheet.DAL.Enums;
using TimeSheet.DAL.Models;
using TimeSheet.DAL.Repositories.Interface;
using TimeSheet.DAL.Utilities;

namespace TimeSheet.Tests.Services
{
    public class DashboardServiceTests
    {
        private const string Me = "emp-1";
        private const string Manager = "mgr-1";
        private const string Director = "dir-1";

        // Business "today" = Tuesday 2026-10-27 (Riyadh); week starts Sunday 2026-10-25.
        private static readonly DateTimeOffset UtcNow = new(2026, 10, 26, 22, 30, 0, TimeSpan.Zero);

        private readonly Mock<IUnitOfWork> _uow = new();
        private readonly Mock<IReportService> _reports = new();
        private readonly List<Project> _projects = new();
        private readonly List<TimeEntry> _entries = new();

        private readonly Project _mine;      // mgr-1 / dir-1, I'm assigned, Active
        private readonly Project _onHold;    // mgr-1 / dir-1, I'm assigned, OnHold
        private readonly Project _removed;   // mgr-1 / dir-1, my assignment removed
        private readonly Project _others;    // mgr-2 / dir-2

        private readonly DashboardService _sut;

        public DashboardServiceTests()
        {
            _mine = AddProject("PRJ-0001", Manager, Director, ProjectStatus.Active, assigned: true);
            _onHold = AddProject("PRJ-0002", Manager, Director, ProjectStatus.OnHold, assigned: true);
            _removed = AddProject("PRJ-0003", Manager, Director, ProjectStatus.Active, assigned: false);
            _others = AddProject("PRJ-0004", "mgr-2", "dir-2", ProjectStatus.Active, assigned: null);

            _uow.SetupRepo(_projects);
            _uow.SetupRepo(_entries);
            _reports.Setup(r => r.GetProjectSummariesAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new List<ProjectSummaryReport>());

            var settings = Options.Create(new TimeSheetSettings
            {
                BusinessTimeZone = "Asia/Riyadh",
                WeekStart = DayOfWeek.Sunday,
                MaxDailyHours = 12,
                MaxBackdateDays = 30
            });
            var clock = new BusinessClock(new FixedTimeProvider(UtcNow), settings);

            _sut = new DashboardService(_uow.Object, new ProjectAccessService(_uow.Object), _reports.Object, clock, settings);
        }

        // ---------------------------------------------------------------- Employee

        [Fact]
        public async Task GetEmployeeDashboardAsync_CountsTodayWeekMonthAndStatuses()
        {
            Add(_mine, "2026-10-27", 4, TimeEntryStatus.PendingManager);    // today
            Add(_onHold, "2026-10-27", 3, TimeEntryStatus.Approved);        // today
            Add(_removed, "2026-10-27", 2, TimeEntryStatus.Rejected);       // today, doesn't count
            Add(_mine, "2026-10-25", 5, TimeEntryStatus.Approved);          // Sunday — this week
            Add(_mine, "2026-10-24", 6, TimeEntryStatus.Approved);          // Saturday — last week
            Add(_mine, "2026-10-20", 1, TimeEntryStatus.PendingDirector);   // pending
            Add(_mine, "2026-10-01", 2, TimeEntryStatus.Approved);          // this month
            Add(_mine, "2026-09-30", 9, TimeEntryStatus.Approved);          // last month
            Add(_mine, "2026-10-26", 8, TimeEntryStatus.Cancelled);         // never counts
            Add(_mine, "2026-10-27", 12, TimeEntryStatus.Approved, employeeId: "emp-2"); // someone else

            var d = await _sut.GetEmployeeDashboardAsync(Me);

            d.Today.Should().Be(new DateOnly(2026, 10, 27));
            d.WeekStart.Should().Be(new DateOnly(2026, 10, 25));
            d.HoursToday.Should().Be(7);
            d.DailyLimit.Should().Be(12);
            d.RemainingToday.Should().Be(5);
            d.HoursThisWeek.Should().Be(12);              // 4 + 3 + 5
            d.ApprovedHoursThisMonth.Should().Be(16);     // 3 + 5 + 6 + 2
            d.PendingCount.Should().Be(2);
            d.RejectedCount.Should().Be(1);
            d.ActiveProjectCount.Should().Be(1);          // OnHold and removed don't count
        }

        [Fact]
        public async Task GetEmployeeDashboardAsync_OverTheCapToday_RemainingIsZero()
        {
            Add(_mine, "2026-10-27", 12, TimeEntryStatus.Approved);

            var d = await _sut.GetEmployeeDashboardAsync(Me);

            d.RemainingToday.Should().Be(0);
        }

        // ---------------------------------------------------------------- Manager

        [Fact]
        public async Task GetManagerDashboardAsync_PendingQueueOnlyHisProjects()
        {
            Add(_mine, "2026-10-20", 4, TimeEntryStatus.PendingManager, submittedAt: "2026-10-21T08:00:00");
            Add(_removed, "2026-10-22", 3, TimeEntryStatus.PendingManager, submittedAt: "2026-10-19T08:00:00");
            Add(_mine, "2026-10-23", 5, TimeEntryStatus.PendingDirector);   // director's queue
            Add(_others, "2026-10-23", 7, TimeEntryStatus.PendingManager);  // other manager

            var d = await _sut.GetManagerDashboardAsync(Manager);

            d.PendingReview.Count.Should().Be(2);
            d.PendingReview.Hours.Should().Be(7);
            d.PendingReview.OldestSubmittedAt.Should().Be(DateTime.Parse("2026-10-19T08:00:00"));
            d.ActiveProjectCount.Should().Be(2); // PRJ-0001 + PRJ-0003 (OnHold excluded)
        }

        [Fact]
        public async Task GetManagerDashboardAsync_EmptyQueue()
        {
            var d = await _sut.GetManagerDashboardAsync(Manager);

            d.PendingReview.Count.Should().Be(0);
            d.PendingReview.Hours.Should().Be(0);
            d.PendingReview.OldestSubmittedAt.Should().BeNull();
        }

        [Fact]
        public async Task GetManagerDashboardAsync_BudgetAlerts_OneBucketPerProject_IgnoringArchived()
        {
            _reports.Setup(r => r.GetProjectSummariesAsync(Manager, AppRoles.Manager)).ReturnsAsync(new List<ProjectSummaryReport>
            {
                Summary(BudgetStatus.Exceeded, projectedOver: true),
                Summary(BudgetStatus.OnTrack, projectedOver: true),
                Summary(BudgetStatus.NearLimit, projectedOver: true),   // projected wins over near-limit
                Summary(BudgetStatus.NearLimit, projectedOver: false),
                Summary(BudgetStatus.OnTrack, projectedOver: false),
                Summary(BudgetStatus.Exceeded, projectedOver: true, ProjectStatus.Archived) // ignored
            });

            var d = await _sut.GetManagerDashboardAsync(Manager);

            d.BudgetAlerts.ExceededCount.Should().Be(1);
            d.BudgetAlerts.ProjectedToExceedCount.Should().Be(2);
            d.BudgetAlerts.NearLimitCount.Should().Be(1);
        }

        // ---------------------------------------------------------------- Director

        [Fact]
        public async Task GetDirectorDashboardAsync_PendingDirectorQueue_AndApprovedThisMonth()
        {
            Add(_mine, "2026-10-20", 4, TimeEntryStatus.PendingDirector);
            Add(_mine, "2026-10-21", 3, TimeEntryStatus.PendingManager);     // manager's queue
            Add(_mine, "2026-10-05", 6, TimeEntryStatus.Approved);
            Add(_removed, "2026-10-26", 2, TimeEntryStatus.Approved);
            Add(_mine, "2026-09-29", 9, TimeEntryStatus.Approved);           // last month
            Add(_others, "2026-10-05", 8, TimeEntryStatus.Approved);         // other director

            var d = await _sut.GetDirectorDashboardAsync(Director);

            d.PendingReview.Count.Should().Be(1);
            d.PendingReview.Hours.Should().Be(4);
            d.ApprovedHoursThisMonth.Should().Be(8);
            d.ActiveProjectCount.Should().Be(2);
        }

        // ------------------------------------------------------------------

        private static ProjectSummaryReport Summary(BudgetStatus status, bool projectedOver,
            ProjectStatus projectStatus = ProjectStatus.Active)
            => new() { BudgetStatus = status, IsProjectedToExceed = projectedOver, ProjectStatus = projectStatus };

        // assigned: true = active assignment for Me, false = removed, null = none
        private Project AddProject(string code, string managerId, string directorId, ProjectStatus status, bool? assigned)
        {
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Code = code,
                Name = code,
                ManagerId = managerId,
                DirectorId = directorId,
                Status = status,
                BudgetHours = 100
            };
            if (assigned is { } isActive)
                project.Assignments.Add(new ProjectAssignment { ProjectId = project.Id, EmployeeId = Me, IsActive = isActive });

            _projects.Add(project);
            return project;
        }

        private void Add(Project project, string date, decimal hours, TimeEntryStatus status,
            string employeeId = Me, string? submittedAt = null)
        {
            _entries.Add(new TimeEntry
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Project = project,
                EmployeeId = employeeId,
                WorkDate = DateOnly.Parse(date),
                Hours = hours,
                Status = status,
                SubmittedAt = submittedAt is null ? UtcNow.UtcDateTime : DateTime.Parse(submittedAt)
            });
        }
    }
}
