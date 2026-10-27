using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using System.Data;
using TimeSheet.BLL.DTOs.Request;
using TimeSheet.BLL.Exceptions;
using TimeSheet.BLL.Services.Classes;
using TimeSheet.BLL.Settings;
using TimeSheet.DAL.Enums;
using TimeSheet.DAL.Models;
using TimeSheet.DAL.Repositories.Interface;

namespace TimeSheet.Tests.Services
{
    public class TimeEntryServiceTests
    {
        private const string Me = "emp-1";
        private const string SomeoneElse = "emp-2";

        // 22:30 UTC on the 26th is already 01:30 on the 27th in Riyadh (UTC+3).
        private static readonly DateTimeOffset UtcNow = new(2026, 10, 26, 22, 30, 0, TimeSpan.Zero);
        private static readonly DateOnly Today = new(2026, 10, 27);

        private readonly Mock<IUnitOfWork> _uow = new();
        private readonly Mock<IGenericRepository<TimeEntry>> _entryRepo;
        private readonly List<Project> _projects = new();
        private readonly List<TimeEntry> _entries = new();
        private readonly List<TimeEntryReview> _reviews = new();

        private readonly Project _active;        // assigned, Active
        private readonly Project _active2;       // assigned, Active
        private readonly Project _onHold;        // assigned, OnHold
        private readonly Project _removedFrom;   // Active, assignment deactivated
        private readonly Project _notAssigned;   // Active, never assigned

        private readonly TimeEntryService _sut;

        public TimeEntryServiceTests()
        {
            _active = AddProject("PRJ-0001", ProjectStatus.Active, assignment: true);
            _active2 = AddProject("PRJ-0002", ProjectStatus.Active, assignment: true);
            _onHold = AddProject("PRJ-0003", ProjectStatus.OnHold, assignment: true);
            _removedFrom = AddProject("PRJ-0004", ProjectStatus.Active, assignment: false);
            _notAssigned = AddProject("PRJ-0005", ProjectStatus.Active, assignment: null);

            _uow.SetupRepo(_projects);
            _entryRepo = _uow.SetupRepo(_entries);
            _entryRepo.Setup(r => r.AddAsync(It.IsAny<TimeEntry>(), It.IsAny<CancellationToken>()))
                .Callback<TimeEntry, CancellationToken>((e, _) =>
                {
                    e.Id = Guid.NewGuid();
                    e.Project = _projects.Single(p => p.Id == e.ProjectId);
                    _entries.Add(e);
                });
            _uow.SetupRepo(_reviews);
            _uow.SetupTransaction<Guid>();
            _uow.SetupTransaction<bool>();

            var settings = Options.Create(new TimeSheetSettings
            {
                BusinessTimeZone = "Asia/Riyadh",
                WeekStart = DayOfWeek.Sunday,
                MaxDailyHours = 12,
                MaxBackdateDays = 30
            });
            var clock = new BusinessClock(new FixedTimeProvider(UtcNow), settings);

            _sut = new TimeEntryService(_uow.Object, new ProjectAccessService(_uow.Object), clock, settings);
        }

        // ---------------------------------------------------------------- Dropdown

        [Fact]
        public async Task GetProjectOptionsAsync_ReturnsOnlyActiveProjectsWithActiveAssignment()
        {
            var result = await _sut.GetProjectOptionsAsync(Me);

            result.Select(p => p.Code).Should().Equal("PRJ-0001", "PRJ-0002");
        }

        // ---------------------------------------------------------------- Submit

        [Fact]
        public async Task SubmitAsync_Valid_CreatesPendingManagerEntry_InSerializableTransaction()
        {
            var result = await _sut.SubmitAsync(Me, Submit(_active, Today.AddDays(-1), 8, "  Workshop  "));

            result.Status.Should().Be(TimeEntryStatus.PendingManager);
            result.SubmittedAt.Should().Be(UtcNow.UtcDateTime);
            result.ProjectCode.Should().Be("PRJ-0001");
            result.Notes.Should().Be("Workshop");
            _entries.Single().EmployeeId.Should().Be(Me);
            _uow.Verify(u => u.ExecuteInTransactionAsync(
                It.IsAny<Func<Task<Guid>>>(), IsolationLevel.Serializable, It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SubmitAsync_TodayInBusinessTimeZone_IsAllowed_EvenIfAheadOfUtcDate()
        {
            var result = await _sut.SubmitAsync(Me, Submit(_active, Today, 4)); // UTC date is still the 26th

            result.WorkDate.Should().Be(Today);
        }

        [Fact]
        public async Task SubmitAsync_FutureDate_ThrowsBadRequest()
        {
            var act = () => _sut.SubmitAsync(Me, Submit(_active, Today.AddDays(1), 4));

            await act.Should().ThrowAsync<BadRequestException>().WithMessage("You can't log hours for a future date.");
        }

        [Fact]
        public async Task SubmitAsync_ExactlyAtBackdateLimit_IsAllowed()
        {
            var result = await _sut.SubmitAsync(Me, Submit(_active, Today.AddDays(-30), 4));

            result.WorkDate.Should().Be(new DateOnly(2026, 9, 27));
        }

        [Fact]
        public async Task SubmitAsync_OlderThanBackdateLimit_ThrowsBadRequest()
        {
            var act = () => _sut.SubmitAsync(Me, Submit(_active, Today.AddDays(-31), 4));

            await act.Should().ThrowAsync<BadRequestException>()
                .WithMessage("You can only log hours for the last 30 days (from 2026-09-27).");
        }

        [Fact]
        public async Task SubmitAsync_MoreThanTwoDecimals_ThrowsBadRequest()
        {
            var act = () => _sut.SubmitAsync(Me, Submit(_active, Today, 1.234m));

            await act.Should().ThrowAsync<BadRequestException>().WithMessage("Hours can have at most 2 decimal places.");
        }

        [Fact]
        public async Task SubmitAsync_ProjectNotAssigned_ThrowsNotFound()
        {
            var act = () => _sut.SubmitAsync(Me, Submit(_notAssigned, Today, 4));

            await act.Should().ThrowAsync<NotFoundException>();
        }

        [Fact]
        public async Task SubmitAsync_RemovedFromProject_ThrowsNotFound()
        {
            var act = () => _sut.SubmitAsync(Me, Submit(_removedFrom, Today, 4));

            await act.Should().ThrowAsync<NotFoundException>();
        }

        [Fact]
        public async Task SubmitAsync_ProjectNotActive_ThrowsBadRequest()
        {
            var act = () => _sut.SubmitAsync(Me, Submit(_onHold, Today, 4));

            await act.Should().ThrowAsync<BadRequestException>().WithMessage("*only be logged on active projects*OnHold*");
        }

        [Theory]
        [InlineData(TimeEntryStatus.PendingManager, "*Edit that entry instead.")]
        [InlineData(TimeEntryStatus.PendingDirector, "*Edit that entry instead.")]
        [InlineData(TimeEntryStatus.Approved, "*Edit that entry instead.")]
        [InlineData(TimeEntryStatus.Rejected, "*rejected entry*Fix and resubmit*")]
        public async Task SubmitAsync_SameProjectSameDay_ThrowsBadRequest(TimeEntryStatus existing, string message)
        {
            AddEntry(Me, _active, Today, 2, existing);

            var act = () => _sut.SubmitAsync(Me, Submit(_active, Today, 2));

            await act.Should().ThrowAsync<BadRequestException>().WithMessage(message);
        }

        [Fact]
        public async Task SubmitAsync_SameProjectSameDay_WhenPreviousWasCancelled_IsAllowed()
        {
            AddEntry(Me, _active, Today, 2, TimeEntryStatus.Cancelled);

            var result = await _sut.SubmitAsync(Me, Submit(_active, Today, 2));

            result.Status.Should().Be(TimeEntryStatus.PendingManager);
        }

        [Fact]
        public async Task SubmitAsync_SameDayOtherEmployee_DoesNotCount()
        {
            AddEntry(SomeoneElse, _active, Today, 12, TimeEntryStatus.Approved);

            var result = await _sut.SubmitAsync(Me, Submit(_active, Today, 12));

            result.Hours.Should().Be(12);
        }

        [Fact]
        public async Task SubmitAsync_OverDailyCapAcrossProjects_ThrowsBadRequest()
        {
            AddEntry(Me, _active2, Today, 8, TimeEntryStatus.PendingDirector);

            var act = () => _sut.SubmitAsync(Me, Submit(_active, Today, 5));

            await act.Should().ThrowAsync<BadRequestException>()
                .WithMessage("Daily limit is 12 h. You already have 8 h on 2026-10-27, so you can add at most 4 h.");
        }

        [Fact]
        public async Task SubmitAsync_ExactlyAtDailyCap_IsAllowed()
        {
            AddEntry(Me, _active2, Today, 8, TimeEntryStatus.Approved);

            var result = await _sut.SubmitAsync(Me, Submit(_active, Today, 4));

            result.Hours.Should().Be(4);
        }

        [Fact]
        public async Task SubmitAsync_RejectedAndCancelledHours_DoNotCountTowardCap()
        {
            AddEntry(Me, _active2, Today, 10, TimeEntryStatus.Rejected);
            AddEntry(Me, _onHold, Today, 10, TimeEntryStatus.Cancelled);

            var result = await _sut.SubmitAsync(Me, Submit(_active, Today, 12));

            result.Hours.Should().Be(12);
        }

        // ---------------------------------------------------------------- Update

        [Fact]
        public async Task UpdateAsync_RejectedEntry_IsResubmittedAsPendingManager()
        {
            var entry = AddEntry(Me, _active, Today.AddDays(-2), 4, TimeEntryStatus.Rejected,
                submittedAt: new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc));

            var result = await _sut.UpdateAsync(Me, entry.Id, Update(_active, entry.WorkDate, 6, "fixed"));

            result.Status.Should().Be(TimeEntryStatus.PendingManager);
            result.Hours.Should().Be(6);
            result.SubmittedAt.Should().Be(UtcNow.UtcDateTime);
        }

        [Fact]
        public async Task UpdateAsync_PendingManagerEntry_KeepsStatusAndSubmittedAt()
        {
            var submittedAt = new DateTime(2026, 10, 25, 8, 0, 0, DateTimeKind.Utc);
            var entry = AddEntry(Me, _active, Today, 4, TimeEntryStatus.PendingManager, submittedAt);

            var result = await _sut.UpdateAsync(Me, entry.Id, Update(_active, Today, 5));

            result.Status.Should().Be(TimeEntryStatus.PendingManager);
            result.SubmittedAt.Should().Be(submittedAt);
        }

        [Theory]
        [InlineData(TimeEntryStatus.PendingDirector)]
        [InlineData(TimeEntryStatus.Approved)]
        [InlineData(TimeEntryStatus.Cancelled)]
        public async Task UpdateAsync_InOtherStatuses_ThrowsBadRequest(TimeEntryStatus status)
        {
            var entry = AddEntry(Me, _active, Today, 4, status);

            var act = () => _sut.UpdateAsync(Me, entry.Id, Update(_active, Today, 5));

            await act.Should().ThrowAsync<BadRequestException>().WithMessage($"Only entries waiting for the manager or rejected can be edited.*{status}*");
        }

        [Fact]
        public async Task UpdateAsync_SomeoneElsesEntry_ThrowsNotFound()
        {
            var entry = AddEntry(SomeoneElse, _active, Today, 4, TimeEntryStatus.PendingManager);

            var act = () => _sut.UpdateAsync(Me, entry.Id, Update(_active, Today, 5));

            await act.Should().ThrowAsync<NotFoundException>().WithMessage("Time entry not found.");
        }

        [Fact]
        public async Task UpdateAsync_DailyCap_DoesNotCountTheEntryItself()
        {
            var entry = AddEntry(Me, _active, Today, 8, TimeEntryStatus.PendingManager);

            var result = await _sut.UpdateAsync(Me, entry.Id, Update(_active, Today, 12));

            result.Hours.Should().Be(12);
        }

        [Fact]
        public async Task UpdateAsync_RejectedEntryOutsideWindow_SameDate_CanStillBeFixed()
        {
            var oldDate = Today.AddDays(-60);
            var entry = AddEntry(Me, _active, oldDate, 4, TimeEntryStatus.Rejected);

            var result = await _sut.UpdateAsync(Me, entry.Id, Update(_active, oldDate, 3));

            result.Status.Should().Be(TimeEntryStatus.PendingManager);
        }

        [Fact]
        public async Task UpdateAsync_MovingToADateOutsideWindow_ThrowsBadRequest()
        {
            var entry = AddEntry(Me, _active, Today, 4, TimeEntryStatus.PendingManager);

            var act = () => _sut.UpdateAsync(Me, entry.Id, Update(_active, Today.AddDays(-45), 4));

            await act.Should().ThrowAsync<BadRequestException>().WithMessage("You can only log hours for the last 30 days*");
        }

        [Fact]
        public async Task UpdateAsync_MovingOntoAnotherEntrysDay_ThrowsBadRequest()
        {
            AddEntry(Me, _active, Today.AddDays(-1), 4, TimeEntryStatus.Approved);
            var entry = AddEntry(Me, _active, Today, 4, TimeEntryStatus.PendingManager);

            var act = () => _sut.UpdateAsync(Me, entry.Id, Update(_active, Today.AddDays(-1), 4));

            await act.Should().ThrowAsync<BadRequestException>().WithMessage("*Edit that entry instead.");
        }

        [Fact]
        public async Task UpdateAsync_WhenRowWasChangedMeanwhile_ThrowsBadRequest()
        {
            var entry = AddEntry(Me, _active, Today, 4, TimeEntryStatus.PendingManager);
            _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DbUpdateConcurrencyException("row version changed"));

            var act = () => _sut.UpdateAsync(Me, entry.Id, Update(_active, Today, 5));

            await act.Should().ThrowAsync<BadRequestException>().WithMessage("This entry was just changed*");
        }

        // ---------------------------------------------------------------- Cancel

        [Fact]
        public async Task CancelAsync_PendingManager_BecomesCancelled()
        {
            var entry = AddEntry(Me, _active, Today, 4, TimeEntryStatus.PendingManager);

            var result = await _sut.CancelAsync(Me, entry.Id);

            result.Status.Should().Be(TimeEntryStatus.Cancelled);
            _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Theory]
        [InlineData(TimeEntryStatus.PendingDirector)]
        [InlineData(TimeEntryStatus.Approved)]
        [InlineData(TimeEntryStatus.Rejected)]
        public async Task CancelAsync_NotPendingManager_ThrowsBadRequest(TimeEntryStatus status)
        {
            var entry = AddEntry(Me, _active, Today, 4, status);

            var act = () => _sut.CancelAsync(Me, entry.Id);

            await act.Should().ThrowAsync<BadRequestException>().WithMessage("Only entries still waiting for the manager can be cancelled.*");
            entry.Status.Should().Be(status);
        }

        [Fact]
        public async Task CancelAsync_WhenReviewedMeanwhile_ThrowsBadRequest()
        {
            var entry = AddEntry(Me, _active, Today, 4, TimeEntryStatus.PendingManager);
            _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DbUpdateConcurrencyException("row version changed"));

            var act = () => _sut.CancelAsync(Me, entry.Id);

            await act.Should().ThrowAsync<BadRequestException>().WithMessage("This entry was just changed*");
        }

        // ---------------------------------------------------------------- My entries

        [Fact]
        public async Task GetMyEntriesAsync_ByDefault_HidesCancelled_AndOthersEntries_NewestFirst()
        {
            AddEntry(Me, _active, Today.AddDays(-2), 4, TimeEntryStatus.Approved);
            AddEntry(Me, _active, Today, 4, TimeEntryStatus.PendingManager);
            AddEntry(Me, _active2, Today, 2, TimeEntryStatus.Cancelled);
            AddEntry(SomeoneElse, _active, Today, 4, TimeEntryStatus.PendingManager);

            var result = (await _sut.GetMyEntriesAsync(Me, new TimeEntryFilterRequest())).Items;

            result.Select(e => e.WorkDate).Should().Equal(Today, Today.AddDays(-2));
        }

        [Fact]
        public async Task GetMyEntriesAsync_FilterCancelled_ShowsThem()
        {
            AddEntry(Me, _active2, Today, 2, TimeEntryStatus.Cancelled);

            var result = (await _sut.GetMyEntriesAsync(Me, new TimeEntryFilterRequest { Status = TimeEntryStatus.Cancelled })).Items;

            result.Should().ContainSingle();
        }

        [Fact]
        public async Task GetMyEntriesAsync_FromAfterTo_ThrowsBadRequest()
        {
            var act = () => _sut.GetMyEntriesAsync(Me, new TimeEntryFilterRequest { From = Today, To = Today.AddDays(-1) });

            await act.Should().ThrowAsync<BadRequestException>();
        }

        // ---------------------------------------------------------------- Rejection info + history

        [Fact]
        public async Task GetMyEntryAsync_RejectedEntry_ShowsLevelAndCommentOfLatestReview()
        {
            var entry = AddEntry(Me, _active, Today, 4, TimeEntryStatus.Rejected);
            AddReview(entry, "Mona Manager", ReviewLevel.Manager, ReviewDecision.Approved, null, minutesAgo: 60);
            AddReview(entry, "Dina Director", ReviewLevel.Director, ReviewDecision.Rejected, "Too many hours", minutesAgo: 5);

            var result = await _sut.GetMyEntryAsync(Me, entry.Id);

            result.RejectedAtLevel.Should().Be(ReviewLevel.Director);
            result.RejectionComment.Should().Be("Too many hours");
        }

        [Fact]
        public async Task GetMyEntryAsync_ReopenedEntry_ShowsDirectorAndReopenComment()
        {
            var entry = AddEntry(Me, _active, Today, 4, TimeEntryStatus.Rejected);
            AddReview(entry, "Mona Manager", ReviewLevel.Manager, ReviewDecision.Approved, null, minutesAgo: 90);
            AddReview(entry, "Dina Director", ReviewLevel.Director, ReviewDecision.Approved, null, minutesAgo: 60);
            AddReview(entry, "Dina Director", ReviewLevel.Director, ReviewDecision.Reopened, "Logged on the wrong project", minutesAgo: 1);

            var result = await _sut.GetMyEntryAsync(Me, entry.Id);

            result.RejectedAtLevel.Should().Be(ReviewLevel.Director);
            result.RejectionComment.Should().Be("Logged on the wrong project");
        }

        [Fact]
        public async Task GetMyEntryAsync_ResubmittedEntry_HasNoRejectionInfo()
        {
            var entry = AddEntry(Me, _active, Today, 4, TimeEntryStatus.PendingManager);
            AddReview(entry, "Mona Manager", ReviewLevel.Manager, ReviewDecision.Rejected, "Split per task", minutesAgo: 30);

            var result = await _sut.GetMyEntryAsync(Me, entry.Id);

            result.RejectedAtLevel.Should().BeNull();
            result.RejectionComment.Should().BeNull();
        }

        [Fact]
        public async Task GetHistoryAsync_ReturnsReviewsOldestFirst()
        {
            var entry = AddEntry(Me, _active, Today, 4, TimeEntryStatus.Approved);
            AddReview(entry, "Dina Director", ReviewLevel.Director, ReviewDecision.Approved, null, minutesAgo: 10);
            AddReview(entry, "Mona Manager", ReviewLevel.Manager, ReviewDecision.Approved, null, minutesAgo: 50);

            var result = await _sut.GetHistoryAsync(Me, entry.Id);

            result.Select(r => r.ReviewerName).Should().Equal("Mona Manager", "Dina Director");
            result.Select(r => r.Level).Should().Equal(ReviewLevel.Manager, ReviewLevel.Director);
        }

        [Fact]
        public async Task GetHistoryAsync_SomeoneElsesEntry_ThrowsNotFound()
        {
            var entry = AddEntry(SomeoneElse, _active, Today, 4, TimeEntryStatus.Approved);

            var act = () => _sut.GetHistoryAsync(Me, entry.Id);

            await act.Should().ThrowAsync<NotFoundException>();
        }

        // ------------------------------------------------------------------

        private void AddReview(TimeEntry entry, string reviewerName, ReviewLevel level, ReviewDecision decision,
            string? comment, int minutesAgo)
        {
            var review = new TimeEntryReview
            {
                Id = Guid.NewGuid(),
                TimeEntryId = entry.Id,
                ReviewerId = reviewerName,
                Reviewer = new ApplicationUser { Id = reviewerName, FullName = reviewerName },
                Level = level,
                Decision = decision,
                Comment = comment,
                CreatedAt = UtcNow.UtcDateTime.AddMinutes(-minutesAgo)
            };
            entry.Reviews.Add(review);
            _reviews.Add(review);
        }

        private static SubmitTimeEntryRequest Submit(Project p, DateOnly date, decimal hours, string? notes = null)
            => new() { ProjectId = p.Id, WorkDate = date, Hours = hours, Notes = notes };

        private static UpdateTimeEntryRequest Update(Project p, DateOnly date, decimal hours, string? notes = null)
            => new() { ProjectId = p.Id, WorkDate = date, Hours = hours, Notes = notes };

        // assignment: true = active, false = removed, null = never assigned
        private Project AddProject(string code, ProjectStatus status, bool? assignment)
        {
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Code = code,
                Name = "Project " + code,
                Status = status,
                DirectorId = "dir-1",
                ManagerId = "mgr-1"
            };
            if (assignment is { } isActive)
                project.Assignments.Add(new ProjectAssignment { ProjectId = project.Id, EmployeeId = Me, IsActive = isActive });

            _projects.Add(project);
            return project;
        }

        private TimeEntry AddEntry(string employeeId, Project project, DateOnly date, decimal hours,
            TimeEntryStatus status, DateTime? submittedAt = null)
        {
            var entry = new TimeEntry
            {
                Id = Guid.NewGuid(),
                EmployeeId = employeeId,
                ProjectId = project.Id,
                Project = project,
                WorkDate = date,
                Hours = hours,
                Status = status,
                SubmittedAt = submittedAt ?? UtcNow.UtcDateTime.AddDays(-1)
            };
            _entries.Add(entry);
            return entry;
        }
    }
}
