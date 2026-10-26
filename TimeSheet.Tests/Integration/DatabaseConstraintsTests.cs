using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TimeSheet.DAL.Enums;
using TimeSheet.DAL.Models;

namespace TimeSheet.Tests.Integration
{
    // The database's own safety nets — what still holds if a bug ever skips a BLL check.
    // These write through EF directly, bypassing the services on purpose.
    [Collection(SqlServerCollection.Name)]
    [Trait("Category", "Integration")]
    public class DatabaseConstraintsTests
    {
        private readonly SqlServerFixture _db;

        public DatabaseConstraintsTests(SqlServerFixture db) => _db = db;

        // ---------------------------------------------------------------- One entry per project per day

        [Theory]
        [InlineData(TimeEntryStatus.PendingManager)]
        [InlineData(TimeEntryStatus.PendingDirector)]
        [InlineData(TimeEntryStatus.Approved)]
        [InlineData(TimeEntryStatus.Rejected)] // a rejected entry still holds its day
        public async Task UniqueIndex_SecondEntrySameEmployeeProjectDay_IsRejected(TimeEntryStatus existing)
        {
            var w = await _db.CreateWorldAsync();
            await Insert(Entry(w, w.ProjectId, existing));

            var act = () => Insert(Entry(w, w.ProjectId, TimeEntryStatus.PendingManager));

            (await act.Should().ThrowAsync<DbUpdateException>())
                .WithInnerException<Microsoft.Data.SqlClient.SqlException>()
                .Where(e => e.Number == 2601 || e.Number == 2627);
        }

        [Fact]
        public async Task UniqueIndex_IsFilteredOnCancelled_SoACancelledEntryFreesTheDay()
        {
            var w = await _db.CreateWorldAsync();
            await Insert(Entry(w, w.ProjectId, TimeEntryStatus.Cancelled));
            await Insert(Entry(w, w.ProjectId, TimeEntryStatus.Cancelled));

            var act = () => Insert(Entry(w, w.ProjectId, TimeEntryStatus.PendingManager));

            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task UniqueIndex_SameDayOtherProjectOrOtherEmployee_IsFine()
        {
            var w = await _db.CreateWorldAsync();
            await Insert(Entry(w, w.ProjectId, TimeEntryStatus.Approved));

            var otherProject = () => Insert(Entry(w, w.Project2Id, TimeEntryStatus.Approved));
            var otherEmployee = () => Insert(Entry(w, w.ProjectId, TimeEntryStatus.Approved, employeeId: w.Employee2Id));

            await otherProject.Should().NotThrowAsync();
            await otherEmployee.Should().NotThrowAsync();
        }

        // ---------------------------------------------------------------- Check constraints

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(12.01)]
        public async Task TimeEntryHours_OutsideZeroToTwelve_IsRejected(double hours)
        {
            var w = await _db.CreateWorldAsync();

            var act = () => Insert(Entry(w, w.ProjectId, TimeEntryStatus.PendingManager, hours: (decimal)hours));

            await act.Should().ThrowAsync<DbUpdateException>();
        }

        [Fact]
        public async Task TimeEntryHours_ExactlyTwelve_IsAllowed()
        {
            var w = await _db.CreateWorldAsync();

            var act = () => Insert(Entry(w, w.ProjectId, TimeEntryStatus.PendingManager, hours: 12));

            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task ProjectBudget_ZeroOrLess_IsRejected()
        {
            var w = await _db.CreateWorldAsync();

            var act = () => Insert(Project(w, budget: 0));

            await act.Should().ThrowAsync<DbUpdateException>();
        }

        [Fact]
        public async Task Project_ManagerEqualToDirector_IsRejected()
        {
            var w = await _db.CreateWorldAsync();

            var act = () => Insert(Project(w, managerId: w.DirectorId));

            await act.Should().ThrowAsync<DbUpdateException>();
        }

        [Fact]
        public async Task Project_EndDateBeforeStartDate_IsRejected()
        {
            var w = await _db.CreateWorldAsync();
            var project = Project(w);
            project.EndDate = project.StartDate.AddDays(-1);

            var act = () => Insert(project);

            await act.Should().ThrowAsync<DbUpdateException>();
        }

        [Theory]
        [InlineData(ReviewDecision.Rejected, null)]
        [InlineData(ReviewDecision.Rejected, "   ")]
        [InlineData(ReviewDecision.Reopened, null)]
        [InlineData(ReviewDecision.Reopened, "")]
        public async Task Review_RejectOrReopenWithoutComment_IsRejected(ReviewDecision decision, string? comment)
        {
            var w = await _db.CreateWorldAsync();
            var entry = Entry(w, w.ProjectId, TimeEntryStatus.Rejected);
            await Insert(entry);

            var act = () => Insert(Review(w, entry.Id, decision, comment));

            await act.Should().ThrowAsync<DbUpdateException>();
        }

        [Theory]
        [InlineData(ReviewDecision.Approved, null)]
        [InlineData(ReviewDecision.Rejected, "Split per task")]
        [InlineData(ReviewDecision.Reopened, "Wrong project")]
        public async Task Review_WithRequiredComment_IsAllowed(ReviewDecision decision, string? comment)
        {
            var w = await _db.CreateWorldAsync();
            var entry = Entry(w, w.ProjectId, TimeEntryStatus.Rejected);
            await Insert(entry);

            var act = () => Insert(Review(w, entry.Id, decision, comment));

            await act.Should().NotThrowAsync();
        }

        // ---------------------------------------------------------------- Unique project name

        [Fact]
        public async Task ProjectName_Duplicate_IsRejected_IgnoringCase()
        {
            var w = await _db.CreateWorldAsync();
            var first = Project(w);
            await Insert(first);

            var copy = Project(w);
            copy.Name = first.Name.ToUpperInvariant(); // SQL Server's default collation is case-insensitive
            var act = () => Insert(copy);

            (await act.Should().ThrowAsync<DbUpdateException>())
                .WithInnerException<Microsoft.Data.SqlClient.SqlException>()
                .Where(e => e.Number == 2601 || e.Number == 2627);
        }

        // ---------------------------------------------------------------- Foreign keys

        [Fact]
        public async Task DeletingAUserWhoManagesAProject_IsRestricted()
        {
            var w = await _db.CreateWorldAsync();

            await using var db = _db.CreateContext();
            db.Users.Remove(await db.Users.SingleAsync(u => u.Id == w.ManagerId));
            var act = () => db.SaveChangesAsync();

            await act.Should().ThrowAsync<DbUpdateException>();
        }

        [Fact]
        public async Task DeletingAUserWithTimeEntries_IsRestricted()
        {
            var w = await _db.CreateWorldAsync();
            await using (var setup = _db.CreateContext())
            {
                // Remove the assignments first so only the time entry still points at the user.
                await setup.ProjectAssignments.Where(a => a.EmployeeId == w.Employee2Id).ExecuteDeleteAsync();
            }
            await Insert(Entry(w, w.ProjectId, TimeEntryStatus.Approved, employeeId: w.Employee2Id));

            await using var db = _db.CreateContext();
            db.Users.Remove(await db.Users.SingleAsync(u => u.Id == w.Employee2Id));
            var act = () => db.SaveChangesAsync();

            await act.Should().ThrowAsync<DbUpdateException>();
        }

        // ---------------------------------------------------------------- RowVersion

        [Fact]
        public async Task RowVersion_SecondWriterWithStaleCopy_GetsConcurrencyException()
        {
            var w = await _db.CreateWorldAsync();
            var entry = Entry(w, w.ProjectId, TimeEntryStatus.PendingManager);
            await Insert(entry);

            await using var first = _db.CreateContext();
            await using var second = _db.CreateContext();
            var a = await first.TimeEntries.SingleAsync(e => e.Id == entry.Id);
            var b = await second.TimeEntries.SingleAsync(e => e.Id == entry.Id);

            a.Status = TimeEntryStatus.PendingDirector;
            await first.SaveChangesAsync();

            b.Status = TimeEntryStatus.Rejected;
            var act = () => second.SaveChangesAsync();

            await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
        }

        [Fact]
        public async Task BaseEntity_GetsIdAndCreatedAt_OnInsert_AndUpdatedAt_OnUpdate()
        {
            var w = await _db.CreateWorldAsync();
            var entry = Entry(w, w.ProjectId, TimeEntryStatus.PendingManager);
            entry.Id = Guid.Empty;
            await Insert(entry);

            entry.Id.Should().NotBeEmpty();
            entry.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
            entry.UpdatedAt.Should().BeNull();

            await using var db = _db.CreateContext();
            var loaded = await db.TimeEntries.SingleAsync(e => e.Id == entry.Id);
            loaded.Hours = 5;
            await db.SaveChangesAsync();

            loaded.UpdatedAt.Should().NotBeNull();
        }

        // ------------------------------------------------------------------

        private async Task Insert(object entity)
        {
            await using var db = _db.CreateContext();
            db.Add(entity);
            await db.SaveChangesAsync();
        }

        private static TimeEntry Entry(World w, Guid projectId, TimeEntryStatus status, decimal hours = 4, string? employeeId = null) => new()
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            EmployeeId = employeeId ?? w.EmployeeId,
            WorkDate = SqlServerFixture.Today.AddDays(-1),
            Hours = hours,
            Status = status,
            SubmittedAt = DateTime.UtcNow
        };

        private static Project Project(World w, decimal budget = 100, string? managerId = null) => new()
        {
            Code = "IT-" + Guid.NewGuid().ToString("N")[..10],
            Name = "Constraint test " + Guid.NewGuid().ToString("N"),
            ClientId = w.ClientId,
            DirectorId = w.DirectorId,
            ManagerId = managerId ?? w.ManagerId,
            BudgetHours = budget,
            StartDate = new DateOnly(2026, 9, 1),
            Status = ProjectStatus.Active
        };

        private static TimeEntryReview Review(World w, Guid entryId, ReviewDecision decision, string? comment) => new()
        {
            TimeEntryId = entryId,
            ReviewerId = w.ManagerId,
            Level = ReviewLevel.Manager,
            Decision = decision,
            Comment = comment
        };
    }
}
