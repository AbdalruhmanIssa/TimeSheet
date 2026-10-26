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

namespace TimeSheet.Tests.Workflow
{
    public enum EntryAction
    {
        ManagerApprove,
        ManagerReject,
        DirectorApprove,
        DirectorReject,
        DirectorReopen,
        EmployeeEdit,
        EmployeeCancel
    }

    // DESIGN §3: every (status × action) pair, allowed and blocked, through the real services.
    //
    //   PendingManager  ── manager approves ──▶ PendingDirector ── director approves ──▶ Approved
    //        │  manager rejects                    │ director rejects                    │ director reopens
    //        ▼                                     ▼                                     ▼
    //     Rejected ◀───────────────────────────────┴─────────────────────────────────────┘
    //        │ employee edits → PendingManager
    //   Cancelled: employee withdraws while PendingManager
    public class TimeEntryWorkflowMatrixTests
    {
        private const string Manager = "mgr-1";
        private const string OtherManager = "mgr-2";
        private const string Director = "dir-1";
        private const string OtherDirector = "dir-2";
        private const string Employee = "emp-1";
        private const string OtherEmployee = "emp-2";

        // Business today = 2026-10-26 (Riyadh).
        private static readonly DateTimeOffset UtcNow = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);

        // The whole state machine. Anything not listed is blocked.
        private static readonly Dictionary<(TimeEntryStatus, EntryAction), TimeEntryStatus> Allowed = new()
        {
            [(TimeEntryStatus.PendingManager, EntryAction.ManagerApprove)] = TimeEntryStatus.PendingDirector,
            [(TimeEntryStatus.PendingManager, EntryAction.ManagerReject)] = TimeEntryStatus.Rejected,
            [(TimeEntryStatus.PendingManager, EntryAction.EmployeeEdit)] = TimeEntryStatus.PendingManager,
            [(TimeEntryStatus.PendingManager, EntryAction.EmployeeCancel)] = TimeEntryStatus.Cancelled,
            [(TimeEntryStatus.PendingDirector, EntryAction.DirectorApprove)] = TimeEntryStatus.Approved,
            [(TimeEntryStatus.PendingDirector, EntryAction.DirectorReject)] = TimeEntryStatus.Rejected,
            [(TimeEntryStatus.Approved, EntryAction.DirectorReopen)] = TimeEntryStatus.Rejected,
            [(TimeEntryStatus.Rejected, EntryAction.EmployeeEdit)] = TimeEntryStatus.PendingManager,
        };

        public static TheoryData<TimeEntryStatus, EntryAction, TimeEntryStatus> AllowedCases()
        {
            var data = new TheoryData<TimeEntryStatus, EntryAction, TimeEntryStatus>();
            foreach (var ((from, action), to) in Allowed)
                data.Add(from, action, to);
            return data;
        }

        public static TheoryData<TimeEntryStatus, EntryAction> BlockedCases()
        {
            var data = new TheoryData<TimeEntryStatus, EntryAction>();
            foreach (var status in Enum.GetValues<TimeEntryStatus>())
                foreach (var action in Enum.GetValues<EntryAction>())
                    if (!Allowed.ContainsKey((status, action)))
                        data.Add(status, action);
            return data;
        }

        private readonly Mock<IUnitOfWork> _uow = new();
        private readonly Dictionary<string, ApplicationUser> _users;
        private readonly List<TimeEntryReview> _reviews = new();
        private readonly Project _project;
        private readonly TimeEntry _entry;
        private readonly ApprovalService _approvals;
        private readonly TimeEntryService _entries;

        public TimeEntryWorkflowMatrixTests()
        {
            _users = new[] { Manager, OtherManager, Director, OtherDirector, Employee, OtherEmployee }
                .Select(id => NewUser(id, id))
                .ToDictionary(u => u.Id);

            _project = new Project
            {
                Id = Guid.NewGuid(),
                Code = "PRJ-0001",
                Name = "Project",
                ManagerId = Manager,
                DirectorId = Director,
                Status = ProjectStatus.Active,
                BudgetHours = 100
            };
            _project.Assignments.Add(new ProjectAssignment { ProjectId = _project.Id, EmployeeId = Employee, IsActive = true });
            // The other employee is on the team too — scoping is about the entry, not the project.
            _project.Assignments.Add(new ProjectAssignment { ProjectId = _project.Id, EmployeeId = OtherEmployee, IsActive = true });

            _entry = new TimeEntry
            {
                Id = Guid.NewGuid(),
                ProjectId = _project.Id,
                Project = _project,
                EmployeeId = Employee,
                Employee = _users[Employee],
                WorkDate = new DateOnly(2026, 10, 25),
                Hours = 4,
                SubmittedAt = UtcNow.UtcDateTime.AddHours(-1)
            };

            _uow.SetupRepo(new List<Project> { _project });
            _uow.SetupRepo(new List<TimeEntry> { _entry });
            var reviewRepo = _uow.SetupRepo(_reviews);
            reviewRepo.Setup(r => r.AddAsync(It.IsAny<TimeEntryReview>(), It.IsAny<CancellationToken>()))
                .Callback<TimeEntryReview, CancellationToken>((r, _) =>
                {
                    r.CreatedAt = UtcNow.UtcDateTime;
                    r.Reviewer = _users[r.ReviewerId];
                    _entry.Reviews.Add(r);
                    _reviews.Add(r);
                });
            _uow.SetupTransaction<bool>();
            _uow.SetupTransaction<Guid>();

            var settings = Options.Create(new TimeSheetSettings
            {
                BusinessTimeZone = "Asia/Riyadh",
                WeekStart = DayOfWeek.Sunday,
                MaxDailyHours = 12,
                MaxBackdateDays = 30
            });
            var clock = new BusinessClock(new FixedTimeProvider(UtcNow), settings);
            var access = new ProjectAccessService(_uow.Object);

            _approvals = new ApprovalService(_uow.Object, access);
            _entries = new TimeEntryService(_uow.Object, access, clock, settings);
        }

        // ---------------------------------------------------------------- Allowed

        [Theory]
        [MemberData(nameof(AllowedCases))]
        public async Task AllowedTransition_MovesToExpectedStatus(TimeEntryStatus from, EntryAction action, TimeEntryStatus to)
        {
            _entry.Status = from;

            await Run(action, inScope: true);

            _entry.Status.Should().Be(to);
        }

        [Theory]
        [MemberData(nameof(AllowedCases))]
        public async Task AllowedTransition_ReviewerActionsWriteOneHistoryRow_EmployeeActionsWriteNone(
            TimeEntryStatus from, EntryAction action, TimeEntryStatus _)
        {
            _entry.Status = from;

            await Run(action, inScope: true);

            var expected = ExpectedReview(action);
            if (expected is null)
            {
                _reviews.Should().BeEmpty();
                return;
            }

            var review = _reviews.Should().ContainSingle().Subject;
            review.Level.Should().Be(expected.Value.Level);
            review.Decision.Should().Be(expected.Value.Decision);
            review.ReviewerId.Should().Be(expected.Value.Level == ReviewLevel.Manager ? Manager : Director);
            review.TimeEntryId.Should().Be(_entry.Id);
        }

        // ---------------------------------------------------------------- Blocked

        [Theory]
        [MemberData(nameof(BlockedCases))]
        public async Task BlockedTransition_IsRefused_AndChangesNothing(TimeEntryStatus from, EntryAction action)
        {
            _entry.Status = from;
            var hoursBefore = _entry.Hours;

            var act = () => Run(action, inScope: true);

            await act.Should().ThrowAsync<BadRequestException>();
            _entry.Status.Should().Be(from);
            _entry.Hours.Should().Be(hoursBefore);
            _reviews.Should().BeEmpty();
            _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public void Matrix_CoversEveryStatusAndAction()
        {
            var total = Enum.GetValues<TimeEntryStatus>().Length * Enum.GetValues<EntryAction>().Length;

            (AllowedCases().Count + BlockedCases().Count).Should().Be(total);
            Allowed.Should().HaveCount(8);
        }

        // ---------------------------------------------------------------- Scoping (rule 11)

        [Theory]
        [MemberData(nameof(AllowedCases))]
        public async Task SameActionByWrongManagerDirectorOrEmployee_IsNotFound_AndChangesNothing(
            TimeEntryStatus from, EntryAction action, TimeEntryStatus _)
        {
            _entry.Status = from;

            var act = () => Run(action, inScope: false);

            await act.Should().ThrowAsync<NotFoundException>();
            _entry.Status.Should().Be(from);
            _reviews.Should().BeEmpty();
        }

        // ---------------------------------------------------------------- Full lifecycle

        [Fact]
        public async Task FullLifecycle_RejectFixApproveReopenFixApprove()
        {
            _entry.Status = TimeEntryStatus.PendingManager;

            await Run(EntryAction.ManagerReject, inScope: true);
            await Run(EntryAction.EmployeeEdit, inScope: true);
            await Run(EntryAction.ManagerApprove, inScope: true);
            await Run(EntryAction.DirectorReject, inScope: true);
            await Run(EntryAction.EmployeeEdit, inScope: true);
            await Run(EntryAction.ManagerApprove, inScope: true);
            await Run(EntryAction.DirectorApprove, inScope: true);
            await Run(EntryAction.DirectorReopen, inScope: true);
            await Run(EntryAction.EmployeeEdit, inScope: true);
            await Run(EntryAction.ManagerApprove, inScope: true);
            await Run(EntryAction.DirectorApprove, inScope: true);

            _entry.Status.Should().Be(TimeEntryStatus.Approved);
            _reviews.Select(r => (r.Level, r.Decision)).Should().Equal(
                (ReviewLevel.Manager, ReviewDecision.Rejected),
                (ReviewLevel.Manager, ReviewDecision.Approved),
                (ReviewLevel.Director, ReviewDecision.Rejected),
                (ReviewLevel.Manager, ReviewDecision.Approved),
                (ReviewLevel.Director, ReviewDecision.Approved),
                (ReviewLevel.Director, ReviewDecision.Reopened),
                (ReviewLevel.Manager, ReviewDecision.Approved),
                (ReviewLevel.Director, ReviewDecision.Approved));
        }

        // ------------------------------------------------------------------

        private Task Run(EntryAction action, bool inScope)
        {
            var manager = inScope ? Manager : OtherManager;
            var director = inScope ? Director : OtherDirector;
            var employee = inScope ? Employee : OtherEmployee;
            var comment = new ReviewRequest { Comment = "Please fix" };

            return action switch
            {
                EntryAction.ManagerApprove => _approvals.ApproveAsync(manager, AppRoles.Manager, _entry.Id),
                EntryAction.ManagerReject => _approvals.RejectAsync(manager, AppRoles.Manager, _entry.Id, comment),
                EntryAction.DirectorApprove => _approvals.ApproveAsync(director, AppRoles.Director, _entry.Id),
                EntryAction.DirectorReject => _approvals.RejectAsync(director, AppRoles.Director, _entry.Id, comment),
                EntryAction.DirectorReopen => _approvals.ReopenAsync(director, _entry.Id, comment),
                EntryAction.EmployeeEdit => _entries.UpdateAsync(employee, _entry.Id, new UpdateTimeEntryRequest
                {
                    ProjectId = _project.Id,
                    WorkDate = _entry.WorkDate,
                    Hours = _entry.Hours + 1,
                    Notes = "edited"
                }),
                EntryAction.EmployeeCancel => _entries.CancelAsync(employee, _entry.Id),
                _ => throw new ArgumentOutOfRangeException(nameof(action))
            };
        }

        private static (ReviewLevel Level, ReviewDecision Decision)? ExpectedReview(EntryAction action) => action switch
        {
            EntryAction.ManagerApprove => (ReviewLevel.Manager, ReviewDecision.Approved),
            EntryAction.ManagerReject => (ReviewLevel.Manager, ReviewDecision.Rejected),
            EntryAction.DirectorApprove => (ReviewLevel.Director, ReviewDecision.Approved),
            EntryAction.DirectorReject => (ReviewLevel.Director, ReviewDecision.Rejected),
            EntryAction.DirectorReopen => (ReviewLevel.Director, ReviewDecision.Reopened),
            _ => null
        };
    }
}
