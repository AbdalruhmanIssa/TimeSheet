using FluentAssertions;
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
    public class ApprovalServiceTests
    {
        private const string Manager1 = "mgr-1";
        private const string Manager2 = "mgr-2";
        private const string Director1 = "dir-1";
        private const string Director2 = "dir-2";
        private const string Employee = "emp-1";

        private readonly Mock<IUnitOfWork> _uow = new();
        private readonly Dictionary<string, DAL.Models.ApplicationUser> _users;
        private readonly List<Project> _projects = new();
        private readonly List<TimeEntry> _entries = new();
        private readonly List<TimeEntryReview> _reviews = new();

        private readonly Project _p1; // mgr-1 / dir-1
        private readonly Project _p2; // mgr-2 / dir-2

        private readonly ApprovalService _sut;

        public ApprovalServiceTests()
        {
            _users = new[]
            {
                NewUser(Manager1, "Mona Manager"), NewUser(Manager2, "Omar Manager"),
                NewUser(Director1, "Dina Director"), NewUser(Director2, "Other Director"),
                NewUser(Employee, "Eve Employee")
            }.ToDictionary(u => u.Id);

            _p1 = AddProject("PRJ-0001", Manager1, Director1, budget: 100);
            _p2 = AddProject("PRJ-0002", Manager2, Director2, budget: 50);

            _uow.SetupRepo(_projects);
            _uow.SetupRepo(_entries);
            var reviewRepo = _uow.SetupRepo(_reviews);
            reviewRepo.Setup(r => r.AddAsync(It.IsAny<TimeEntryReview>(), It.IsAny<CancellationToken>()))
                .Callback<TimeEntryReview, CancellationToken>((r, _) =>
                {
                    r.Id = Guid.NewGuid();
                    r.CreatedAt = DateTime.UtcNow;
                    r.Reviewer = _users[r.ReviewerId];
                    _entries.Single(e => e.Id == r.TimeEntryId).Reviews.Add(r);
                    _reviews.Add(r);
                });
            _uow.SetupTransaction<bool>();

            _sut = new ApprovalService(_uow.Object, new ProjectAccessService(_uow.Object));
        }

        // ---------------------------------------------------------------- Pending lists

        [Fact]
        public async Task GetPendingAsync_Manager_SeesOnlyPendingManagerOnHisProjects_OldestFirst()
        {
            var newer = AddEntry(_p1, TimeEntryStatus.PendingManager, submittedMinutesAgo: 10);
            var older = AddEntry(_p1, TimeEntryStatus.PendingManager, submittedMinutesAgo: 60);
            AddEntry(_p1, TimeEntryStatus.PendingDirector);
            AddEntry(_p1, TimeEntryStatus.Rejected);
            AddEntry(_p2, TimeEntryStatus.PendingManager); // other manager's project

            var result = (await _sut.GetPendingAsync(Manager1, AppRoles.Manager, new PagedRequest())).Items;

            result.Select(e => e.Id).Should().Equal(older.Id, newer.Id);
            result.Should().OnlyContain(e => e.EmployeeName == "Eve Employee" && e.BudgetImpact == null);
        }

        [Fact]
        public async Task GetPendingAsync_Director_SeesOnlyPendingDirectorOnHisProjects()
        {
            var mine = AddEntry(_p1, TimeEntryStatus.PendingDirector);
            AddEntry(_p1, TimeEntryStatus.PendingManager);
            AddEntry(_p2, TimeEntryStatus.PendingDirector); // other director's project

            var result = (await _sut.GetPendingAsync(Director1, AppRoles.Director, new PagedRequest())).Items;

            result.Should().ContainSingle(e => e.Id == mine.Id);
        }

        [Fact]
        public async Task GetPendingAsync_Director_ShowsBudgetImpactPerEntry()
        {
            AddEntry(_p1, TimeEntryStatus.Approved, hours: 12, daysAgo: 1);
            AddEntry(_p1, TimeEntryStatus.Approved, hours: 83, daysAgo: 2); // 95 of 100 used
            AddEntry(_p1, TimeEntryStatus.Rejected, hours: 10, daysAgo: 3); // doesn't count
            var over = AddEntry(_p1, TimeEntryStatus.PendingDirector, hours: 8, daysAgo: 4, submittedMinutesAgo: 30);
            var fits = AddEntry(_p1, TimeEntryStatus.PendingDirector, hours: 4, daysAgo: 5, submittedMinutesAgo: 20);

            var result = (await _sut.GetPendingAsync(Director1, AppRoles.Director, new PagedRequest())).Items;

            var overImpact = result.Single(e => e.Id == over.Id).BudgetImpact!;
            overImpact.BudgetHours.Should().Be(100);
            overImpact.ActualHours.Should().Be(95);
            overImpact.RemainingHours.Should().Be(5);
            overImpact.RemainingAfterApproval.Should().Be(-3);
            overImpact.WouldExceedBudget.Should().BeTrue();
            overImpact.ExceedsBudgetBy.Should().Be(3);

            var fitsImpact = result.Single(e => e.Id == fits.Id).BudgetImpact!;
            fitsImpact.RemainingAfterApproval.Should().Be(1);
            fitsImpact.WouldExceedBudget.Should().BeFalse();
            fitsImpact.ExceedsBudgetBy.Should().Be(0);
        }

        [Fact]
        public async Task GetPendingAsync_Director_ShowsWhichManagerApproved()
        {
            var entry = AddEntry(_p1, TimeEntryStatus.PendingDirector);
            AddReview(entry, Manager1, ReviewLevel.Manager, ReviewDecision.Approved);

            var result = (await _sut.GetPendingAsync(Director1, AppRoles.Director, new PagedRequest())).Items;

            result.Single().ManagerApprovedBy.Should().Be("Mona Manager");
            result.Single().ManagerApprovedAt.Should().NotBeNull();
        }

        // ---------------------------------------------------------------- Manager actions

        [Fact]
        public async Task ApproveAsync_Manager_MovesToPendingDirector_AndRecordsReview()
        {
            var entry = AddEntry(_p1, TimeEntryStatus.PendingManager);

            var result = await _sut.ApproveAsync(Manager1, AppRoles.Manager, entry.Id);

            result.Status.Should().Be(TimeEntryStatus.PendingDirector);
            var review = _reviews.Single();
            review.Level.Should().Be(ReviewLevel.Manager);
            review.Decision.Should().Be(ReviewDecision.Approved);
            review.ReviewerId.Should().Be(Manager1);
            review.Comment.Should().BeNull();
            _uow.Verify(u => u.ExecuteInTransactionAsync(It.IsAny<Func<Task<bool>>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task ApproveAsync_Manager_OnAnotherManagersProject_ThrowsNotFound()
        {
            var entry = AddEntry(_p2, TimeEntryStatus.PendingManager);

            var act = () => _sut.ApproveAsync(Manager1, AppRoles.Manager, entry.Id);

            await act.Should().ThrowAsync<NotFoundException>().WithMessage("Time entry not found.");
            entry.Status.Should().Be(TimeEntryStatus.PendingManager);
        }

        [Theory]
        [InlineData(TimeEntryStatus.PendingDirector)]
        [InlineData(TimeEntryStatus.Approved)]
        [InlineData(TimeEntryStatus.Rejected)]
        public async Task ApproveAsync_Manager_WhenNotPendingManager_ThrowsBadRequest(TimeEntryStatus status)
        {
            var entry = AddEntry(_p1, status);

            var act = () => _sut.ApproveAsync(Manager1, AppRoles.Manager, entry.Id);

            await act.Should().ThrowAsync<BadRequestException>()
                .WithMessage($"This entry is not waiting for manager review. It is {status}.");
            _reviews.Should().BeEmpty();
        }

        [Fact]
        public async Task ApproveAsync_CancelledEntry_ThrowsBadRequest()
        {
            var entry = AddEntry(_p1, TimeEntryStatus.Cancelled);

            var act = () => _sut.ApproveAsync(Manager1, AppRoles.Manager, entry.Id);

            await act.Should().ThrowAsync<BadRequestException>().WithMessage("This entry was cancelled by the employee.");
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public async Task RejectAsync_WithoutComment_ThrowsBadRequest(string comment)
        {
            var entry = AddEntry(_p1, TimeEntryStatus.PendingManager);

            var act = () => _sut.RejectAsync(Manager1, AppRoles.Manager, entry.Id, new ReviewRequest { Comment = comment });

            await act.Should().ThrowAsync<BadRequestException>().WithMessage("Rejecting an entry requires a comment.");
            entry.Status.Should().Be(TimeEntryStatus.PendingManager);
        }

        [Fact]
        public async Task RejectAsync_Manager_MovesToRejected_WithTrimmedComment()
        {
            var entry = AddEntry(_p1, TimeEntryStatus.PendingManager);

            var result = await _sut.RejectAsync(Manager1, AppRoles.Manager, entry.Id,
                new ReviewRequest { Comment = "  Split per task  " });

            result.Status.Should().Be(TimeEntryStatus.Rejected);
            _reviews.Single().Should().Match<TimeEntryReview>(r =>
                r.Level == ReviewLevel.Manager && r.Decision == ReviewDecision.Rejected && r.Comment == "Split per task");
        }

        // ---------------------------------------------------------------- Director actions

        [Fact]
        public async Task ApproveAsync_Director_MovesToApproved()
        {
            var entry = AddEntry(_p1, TimeEntryStatus.PendingDirector);

            var result = await _sut.ApproveAsync(Director1, AppRoles.Director, entry.Id);

            result.Status.Should().Be(TimeEntryStatus.Approved);
            _reviews.Single().Level.Should().Be(ReviewLevel.Director);
        }

        [Fact]
        public async Task ApproveAsync_Director_WhenStillPendingManager_ThrowsBadRequest()
        {
            var entry = AddEntry(_p1, TimeEntryStatus.PendingManager);

            var act = () => _sut.ApproveAsync(Director1, AppRoles.Director, entry.Id);

            await act.Should().ThrowAsync<BadRequestException>()
                .WithMessage("This entry is not waiting for director review. It is PendingManager.");
        }

        [Fact]
        public async Task ApproveAsync_Director_OnAnotherDirectorsProject_ThrowsNotFound()
        {
            var entry = AddEntry(_p2, TimeEntryStatus.PendingDirector);

            var act = () => _sut.ApproveAsync(Director1, AppRoles.Director, entry.Id);

            await act.Should().ThrowAsync<NotFoundException>();
        }

        [Fact]
        public async Task RejectAsync_Director_MovesToRejected()
        {
            var entry = AddEntry(_p1, TimeEntryStatus.PendingDirector);

            var result = await _sut.RejectAsync(Director1, AppRoles.Director, entry.Id, new ReviewRequest { Comment = "Over budget" });

            result.Status.Should().Be(TimeEntryStatus.Rejected);
            _reviews.Single().Level.Should().Be(ReviewLevel.Director);
        }

        [Fact]
        public async Task ReopenAsync_ApprovedEntry_BecomesRejected_WithReopenedReview()
        {
            var entry = AddEntry(_p1, TimeEntryStatus.Approved);

            var result = await _sut.ReopenAsync(Director1, entry.Id, new ReviewRequest { Comment = "Wrong project" });

            result.Status.Should().Be(TimeEntryStatus.Rejected);
            _reviews.Single().Should().Match<TimeEntryReview>(r =>
                r.Level == ReviewLevel.Director && r.Decision == ReviewDecision.Reopened && r.Comment == "Wrong project");
        }

        [Fact]
        public async Task ReopenAsync_NotApproved_ThrowsBadRequest()
        {
            var entry = AddEntry(_p1, TimeEntryStatus.PendingDirector);

            var act = () => _sut.ReopenAsync(Director1, entry.Id, new ReviewRequest { Comment = "x" });

            await act.Should().ThrowAsync<BadRequestException>()
                .WithMessage("Only approved entries can be reopened. This entry is PendingDirector.");
        }

        [Fact]
        public async Task ReopenAsync_WithoutComment_ThrowsBadRequest()
        {
            var entry = AddEntry(_p1, TimeEntryStatus.Approved);

            var act = () => _sut.ReopenAsync(Director1, entry.Id, new ReviewRequest { Comment = " " });

            await act.Should().ThrowAsync<BadRequestException>().WithMessage("Reopening an entry requires a comment.");
        }

        [Fact]
        public async Task ReopenAsync_AnotherDirectorsProject_ThrowsNotFound()
        {
            var entry = AddEntry(_p2, TimeEntryStatus.Approved);

            var act = () => _sut.ReopenAsync(Director1, entry.Id, new ReviewRequest { Comment = "x" });

            await act.Should().ThrowAsync<NotFoundException>();
        }

        // ---------------------------------------------------------------- Concurrency

        [Fact]
        public async Task ApproveAsync_WhenRowVersionConflicts_ThrowsAlreadyReviewed()
        {
            var entry = AddEntry(_p1, TimeEntryStatus.PendingManager);
            _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DbUpdateConcurrencyException("row version changed"));

            var act = () => _sut.ApproveAsync(Manager1, AppRoles.Manager, entry.Id);

            await act.Should().ThrowAsync<BadRequestException>().WithMessage("This entry was already reviewed.");
        }

        // ---------------------------------------------------------------- Bulk

        [Fact]
        public async Task BulkApproveAsync_ProcessesEachEntryOnItsOwn()
        {
            var ok1 = AddEntry(_p1, TimeEntryStatus.PendingManager, daysAgo: 1);
            var wrongStatus = AddEntry(_p1, TimeEntryStatus.Approved, daysAgo: 2);
            var otherProject = AddEntry(_p2, TimeEntryStatus.PendingManager, daysAgo: 3);
            var ok2 = AddEntry(_p1, TimeEntryStatus.PendingManager, daysAgo: 4);
            var missing = Guid.NewGuid();

            var result = await _sut.BulkApproveAsync(Manager1, AppRoles.Manager, new BulkReviewRequest
            {
                Ids = new() { ok1.Id, wrongStatus.Id, otherProject.Id, missing, ok2.Id, ok1.Id } // ok1 twice
            });

            result.Approved.Should().Equal(ok1.Id, ok2.Id);
            result.Failed.Select(f => f.Id).Should().Equal(wrongStatus.Id, otherProject.Id, missing);
            result.Failed[0].Reason.Should().Be("This entry is not waiting for manager review. It is Approved.");
            result.Failed[1].Reason.Should().Be("Time entry not found.");
            ok1.Status.Should().Be(TimeEntryStatus.PendingDirector);
            ok2.Status.Should().Be(TimeEntryStatus.PendingDirector);
            _uow.Verify(u => u.ClearChanges(), Times.Exactly(3));
        }

        [Fact]
        public async Task BulkApproveAsync_ConcurrencyFailureOnOne_DoesNotStopTheOthers()
        {
            var first = AddEntry(_p1, TimeEntryStatus.PendingManager, daysAgo: 1);
            var second = AddEntry(_p1, TimeEntryStatus.PendingManager, daysAgo: 2);
            _uow.SetupSequence(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new DbUpdateConcurrencyException("row version changed"))
                .ReturnsAsync(1);

            var result = await _sut.BulkApproveAsync(Manager1, AppRoles.Manager,
                new BulkReviewRequest { Ids = new() { first.Id, second.Id } });

            result.Failed.Should().ContainSingle(f => f.Id == first.Id && f.Reason == "This entry was already reviewed.");
            result.Approved.Should().Equal(second.Id);
        }

        // ------------------------------------------------------------------

        private Project AddProject(string code, string managerId, string directorId, decimal budget)
        {
            var project = new Project
            {
                Id = Guid.NewGuid(),
                Code = code,
                Name = "Project " + code,
                ManagerId = managerId,
                DirectorId = directorId,
                BudgetHours = budget,
                Status = ProjectStatus.Active
            };
            _projects.Add(project);
            return project;
        }

        private TimeEntry AddEntry(Project project, TimeEntryStatus status, decimal hours = 4,
            int daysAgo = 1, int submittedMinutesAgo = 5)
        {
            var entry = new TimeEntry
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Project = project,
                EmployeeId = Employee,
                Employee = _users[Employee],
                WorkDate = new DateOnly(2026, 10, 26).AddDays(-daysAgo),
                Hours = hours,
                Status = status,
                SubmittedAt = DateTime.UtcNow.AddMinutes(-submittedMinutesAgo)
            };
            _entries.Add(entry);
            return entry;
        }

        private void AddReview(TimeEntry entry, string reviewerId, ReviewLevel level, ReviewDecision decision, string? comment = null)
        {
            var review = new TimeEntryReview
            {
                Id = Guid.NewGuid(),
                TimeEntryId = entry.Id,
                ReviewerId = reviewerId,
                Reviewer = _users[reviewerId],
                Level = level,
                Decision = decision,
                Comment = comment,
                CreatedAt = DateTime.UtcNow.AddMinutes(-1)
            };
            entry.Reviews.Add(review);
            _reviews.Add(review);
        }
    }
}
