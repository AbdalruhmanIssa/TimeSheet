using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TimeSheet.BLL.DTOs.Request;
using TimeSheet.BLL.Exceptions;
using TimeSheet.DAL.Enums;
using TimeSheet.DAL.Models;
using TimeSheet.DAL.Utilities;

namespace TimeSheet.Tests.Integration
{
    // Two requests at the same moment. Each round runs on fresh users/projects and
    // two separate DbContexts — exactly what two HTTP requests would get.
    [Collection(SqlServerCollection.Name)]
    [Trait("Category", "Integration")]
    public class ConcurrencyTests
    {
        private const int Rounds = 3;
        private readonly SqlServerFixture _db;

        public ConcurrencyTests(SqlServerFixture db) => _db = db;

        [Fact]
        public async Task DailyCap_TwoSubmitsAtOnceOnDifferentProjects_OnlyOneFits()
        {
            for (var round = 0; round < Rounds; round++)
            {
                var w = await _db.CreateWorldAsync();
                var day = SqlServerFixture.Today.AddDays(-1);

                // 8 h + 8 h > 12 h: both checks pass if run without isolation — serializable must stop one.
                var outcomes = await RunTogether(
                    s => s.Entries.SubmitAsync(w.EmployeeId, Submit(w.ProjectId, day, 8)),
                    s => s.Entries.SubmitAsync(w.EmployeeId, Submit(w.Project2Id, day, 8)));

                outcomes.Count(o => o is null).Should().Be(1, $"round {round}: exactly one submit must succeed");
                outcomes.Single(o => o is not null).Should().BeOfType<BadRequestException>()
                    .Which.Message.Should().StartWith("Daily limit is 12 h");

                await using var check = _db.CreateContext();
                (await check.TimeEntries.Where(e => e.EmployeeId == w.EmployeeId && e.WorkDate == day).SumAsync(e => e.Hours))
                    .Should().Be(8);
            }
        }

        [Fact]
        public async Task DuplicateDay_TwoSubmitsAtOnceOnTheSameProject_OnlyOneIsSaved()
        {
            for (var round = 0; round < Rounds; round++)
            {
                var w = await _db.CreateWorldAsync();
                var day = SqlServerFixture.Today.AddDays(-2);

                var outcomes = await RunTogether(
                    s => s.Entries.SubmitAsync(w.EmployeeId, Submit(w.ProjectId, day, 2)),
                    s => s.Entries.SubmitAsync(w.EmployeeId, Submit(w.ProjectId, day, 3)));

                outcomes.Count(o => o is null).Should().Be(1, $"round {round}: exactly one submit must succeed");
                // A clean 400 — never a raw unique-index error (500).
                outcomes.Single(o => o is not null).Should().BeOfType<BadRequestException>()
                    .Which.Message.Should().Contain("You already have an entry");

                await using var check = _db.CreateContext();
                (await check.TimeEntries.CountAsync(e => e.EmployeeId == w.EmployeeId && e.WorkDate == day)).Should().Be(1);
            }
        }

        [Fact]
        public async Task Review_TwoApprovalsOfTheSameEntryAtOnce_OnlyOneCounts()
        {
            for (var round = 0; round < Rounds; round++)
            {
                var w = await _db.CreateWorldAsync();
                Guid entryId;
                await using (var s = _db.CreateServices())
                    entryId = (await s.Entries.SubmitAsync(w.EmployeeId, Submit(w.ProjectId, SqlServerFixture.Today, 4))).Id;

                var outcomes = await RunTogether(
                    s => s.Approvals.ApproveAsync(w.ManagerId, AppRoles.Manager, entryId),
                    s => s.Approvals.ApproveAsync(w.ManagerId, AppRoles.Manager, entryId));

                outcomes.Count(o => o is null).Should().Be(1, $"round {round}: exactly one approval must succeed");
                // Either the RowVersion conflict or the status check caught it — both are a clean 400.
                outcomes.Single(o => o is not null).Should().BeOfType<BadRequestException>()
                    .Which.Message.Should().Match(m =>
                        m == "This entry was already reviewed." ||
                        m == "This entry is not waiting for manager review. It is PendingDirector.");

                await using var check = _db.CreateContext();
                (await check.TimeEntries.SingleAsync(e => e.Id == entryId)).Status.Should().Be(TimeEntryStatus.PendingDirector);
                (await check.TimeEntryReviews.CountAsync(r => r.TimeEntryId == entryId)).Should().Be(1);
            }
        }

        [Fact]
        public async Task EditVersusReview_AtOnce_EndsInAConsistentState()
        {
            for (var round = 0; round < Rounds; round++)
            {
                var w = await _db.CreateWorldAsync();
                Guid entryId;
                await using (var s = _db.CreateServices())
                    entryId = (await s.Entries.SubmitAsync(w.EmployeeId, Submit(w.ProjectId, SqlServerFixture.Today, 4))).Id;

                var outcomes = await RunTogether(
                    s => s.Entries.UpdateAsync(w.EmployeeId, entryId, new UpdateTimeEntryRequest
                    {
                        ProjectId = w.ProjectId,
                        WorkDate = SqlServerFixture.Today,
                        Hours = 6
                    }),
                    s => s.Approvals.ApproveAsync(w.ManagerId, AppRoles.Manager, entryId));

                outcomes.Should().OnlyContain(o => o == null || o is BadRequestException);

                await using var check = _db.CreateContext();
                var entry = await check.TimeEntries.SingleAsync(e => e.Id == entryId);
                var reviews = await check.TimeEntryReviews.CountAsync(r => r.TimeEntryId == entryId);

                if (outcomes[1] is null)
                {
                    // Approval landed: status moved on, exactly one review row.
                    entry.Status.Should().Be(TimeEntryStatus.PendingDirector);
                    reviews.Should().Be(1);
                }
                else
                {
                    entry.Status.Should().Be(TimeEntryStatus.PendingManager);
                    reviews.Should().Be(0);
                }

                // The manager never approves hours different from what was on the entry when he approved:
                // if both succeeded, the edit came first (6 h) and the approval saw it.
                if (outcomes[0] is null && outcomes[1] is null)
                    entry.Hours.Should().Be(6);
            }
        }

        [Fact]
        public async Task ProjectCodes_ManyCreatesAtOnce_AreAllDistinct()
        {
            var w = await _db.CreateWorldAsync();

            var creates = Enumerable.Range(0, 5).Select(async i =>
            {
                await using var s = _db.CreateServices();
                return (await s.Projects.CreateAsync(w.DirectorId, new CreateProjectRequest
                {
                    Name = $"Parallel {i}",
                    ClientId = w.ClientId,
                    ManagerId = w.ManagerId,
                    BudgetHours = 10,
                    StartDate = new DateOnly(2026, 10, 1)
                })).Code;
            });

            var codes = await Task.WhenAll(creates);

            codes.Should().OnlyHaveUniqueItems();
            codes.Should().OnlyContain(c => c.StartsWith("PRJ-"));
        }

        [Fact]
        public async Task ProjectName_TwoCreatesWithTheSameNameAtOnce_OnlyOneIsSaved()
        {
            for (var round = 0; round < Rounds; round++)
            {
                var w = await _db.CreateWorldAsync();
                var name = $"Same name {Guid.NewGuid():N}";

                CreateProjectRequest Request() => new()
                {
                    Name = name,
                    ClientId = w.ClientId,
                    ManagerId = w.ManagerId,
                    BudgetHours = 10,
                    StartDate = new DateOnly(2026, 10, 1)
                };

                // Two different directors, same name — names are unique across the system.
                var outcomes = await RunTogether(
                    s => s.Projects.CreateAsync(w.DirectorId, Request()),
                    s => s.Projects.CreateAsync(w.OtherDirectorId, Request()));

                outcomes.Count(o => o is null).Should().Be(1, $"round {round}: exactly one create must succeed");
                outcomes.Single(o => o is not null).Should().BeOfType<BadRequestException>()
                    .Which.Message.Should().StartWith($"A project named '{name}' already exists");

                await using var check = _db.CreateContext();
                (await check.Projects.CountAsync(p => p.Name == name)).Should().Be(1);
            }
        }

        // ------------------------------------------------------------------

        // Starts both operations at the same moment on separate service instances.
        // Returns null for success or the exception thrown, in the same order.
        private async Task<Exception?[]> RunTogether(params Func<Services, Task>[] operations)
        {
            using var start = new ManualResetEventSlim(false);

            var tasks = operations.Select(op => Task.Run(async () =>
            {
                await using var services = _db.CreateServices();
                await services.Db.Database.OpenConnectionAsync(); // connect before the start signal
                start.Wait();
                try
                {
                    await op(services);
                    return (Exception?)null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            })).ToArray();

            start.Set();
            return await Task.WhenAll(tasks);
        }

        private static SubmitTimeEntryRequest Submit(Guid projectId, DateOnly day, decimal hours)
            => new() { ProjectId = projectId, WorkDate = day, Hours = hours, Notes = "concurrency test" };
    }
}
