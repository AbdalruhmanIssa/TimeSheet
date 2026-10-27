using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TimeSheet.BLL.DTOs.Request;
using TimeSheet.DAL.Data;
using TimeSheet.DAL.Enums;
using TimeSheet.DAL.Models;
using TimeSheet.DAL.Utilities;

namespace TimeSheet.Tests.Integration
{
    // The demo data is what an interviewer sees — it must obey every rule it demonstrates,
    // and every case it promises (exceeded, projected over, reopened, ...) must really be there.
    // Seeds its own fresh database exactly like Program.cs does (Identity + SeedData).
    [Trait("Category", "Integration")]
    public class DemoSeedTests : IAsyncLifetime
    {
        private readonly SqlServerFixture _db = new();
        private Dictionary<string, string> _ids = new();

        // Business-time-zone date, the same value Program.cs passes (IBusinessClock.Today).
        private static readonly DateOnly Today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Asia/Riyadh"));

        public async Task InitializeAsync()
        {
            await _db.InitializeAsync();
            await RunSeedAsync();
            await using var db = _db.CreateContext();
            _ids = await db.Users.ToDictionaryAsync(u => u.UserName!, u => u.Id);
        }

        public Task DisposeAsync() => _db.DisposeAsync();

        // ---------------------------------------------------------------- People and projects

        [Fact]
        public async Task Seeds_2Directors_5Managers_10Employees_And1Admin_WithWorkingPasswords()
        {
            await using var db = _db.CreateContext();
            var perRole = await db.UserRoles
                .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
                .GroupBy(n => n).Select(g => new { Role = g.Key!, Count = g.Count() })
                .ToDictionaryAsync(x => x.Role, x => x.Count);

            perRole.Should().BeEquivalentTo(new Dictionary<string, int>
            {
                [AppRoles.Admin] = 1, [AppRoles.Director] = 2, [AppRoles.Manager] = 5, [AppRoles.Employee] = 10
            });

            await using var provider = BuildIdentity();
            using var scope = provider.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            foreach (var (userName, role) in new[] { ("admin", AppRoles.Admin), ("khaled", AppRoles.Director), ("omar", AppRoles.Manager), ("ahmad", AppRoles.Employee) })
                (await users.CheckPasswordAsync((await users.FindByNameAsync(userName))!, SeedData.DemoPassword(role)))
                    .Should().BeTrue($"{userName} must log in with {SeedData.DemoPassword(role)}");
        }

        [Fact]
        public async Task Seeds_6Clients_And10Projects_InEveryStatus()
        {
            await using var db = _db.CreateContext();

            (await db.Clients.CountAsync()).Should().Be(6);
            (await db.Clients.Where(c => !c.IsActive).Select(c => c.Name).ToListAsync()).Should().Equal("Oasis Construction");

            var statuses = await db.Projects.OrderBy(p => p.Code).Select(p => new { p.Code, p.Status }).ToListAsync();
            statuses.Should().HaveCount(10);
            statuses.Count(p => p.Status == ProjectStatus.Active).Should().Be(7);
            statuses.Single(p => p.Status == ProjectStatus.OnHold).Code.Should().Be("PRJ-0006");
            statuses.Single(p => p.Status == ProjectStatus.Completed).Code.Should().Be("PRJ-0005");
            statuses.Single(p => p.Status == ProjectStatus.Archived).Code.Should().Be("PRJ-0008");
        }

        // ---------------------------------------------------------------- The data obeys the rules

        [Fact]
        public async Task EveryEntry_IsInThePast_OnAProjectTheEmployeeWasAssignedTo_AndWithinTheProjectDates()
        {
            await using var db = _db.CreateContext();

            var entries = await db.TimeEntries
                .Select(e => new
                {
                    e.Project.Code,
                    e.WorkDate,
                    e.Project.StartDate,
                    e.Project.EndDate,
                    WasAssigned = e.Project.Assignments.Any(a => a.EmployeeId == e.EmployeeId)
                })
                .ToListAsync();

            entries.Should().NotBeEmpty();
            entries.Should().OnlyContain(e => e.WorkDate < Today);
            entries.Should().OnlyContain(e => e.WasAssigned);
            entries.Should().OnlyContain(e => e.WorkDate >= e.StartDate && (e.EndDate == null || e.WorkDate <= e.EndDate));
        }

        [Fact]
        public async Task NewestEntry_IsOnTheLastWorkdayBeforeTheBusinessToday()
        {
            await using var db = _db.CreateContext();

            var lastWorkday = Today.AddDays(-1);
            while (lastWorkday.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday)
                lastWorkday = lastWorkday.AddDays(-1);

            (await db.TimeEntries.MaxAsync(e => e.WorkDate)).Should().Be(lastWorkday);
        }

        [Fact]
        public async Task NoEmployee_IsOverTheDailyCap()
        {
            await using var db = _db.CreateContext();

            var worstDay = await db.TimeEntries
                .Where(e => e.Status == TimeEntryStatus.PendingManager || e.Status == TimeEntryStatus.PendingDirector || e.Status == TimeEntryStatus.Approved)
                .GroupBy(e => new { e.EmployeeId, e.WorkDate })
                .Select(g => g.Sum(e => e.Hours))
                .MaxAsync();

            worstDay.Should().BeLessThanOrEqualTo(12);
        }

        [Fact]
        public async Task EveryEntry_HasTheReviewHistoryItsStatusNeeds_FromTheRightPeople()
        {
            await using var db = _db.CreateContext();
            var entries = await db.TimeEntries
                .Include(e => e.Project)
                .Include(e => e.Reviews)
                .AsSplitQuery()
                .ToListAsync();

            foreach (var e in entries)
            {
                var history = e.Reviews.OrderBy(r => r.CreatedAt).ToList();
                var steps = history.Select(r => (r.Level, r.Decision)).ToList();
                var because = $"{e.Project.Code} {e.WorkDate} is {e.Status}";

                switch (e.Status)
                {
                    case TimeEntryStatus.Approved:
                        steps.TakeLast(2).Should().Equal(new[] { (ReviewLevel.Manager, ReviewDecision.Approved), (ReviewLevel.Director, ReviewDecision.Approved) }, because);
                        break;
                    case TimeEntryStatus.PendingDirector:
                        steps.Last().Should().Be((ReviewLevel.Manager, ReviewDecision.Approved), because);
                        break;
                    case TimeEntryStatus.Rejected:
                        history.Last().Decision.Should().BeOneOf(new[] { ReviewDecision.Rejected, ReviewDecision.Reopened }, because);
                        history.Last().Comment.Should().NotBeNullOrWhiteSpace(because);
                        break;
                    case TimeEntryStatus.PendingManager:
                    case TimeEntryStatus.Cancelled:
                        // Never reviewed, or resubmitted after a rejection.
                        if (history.Count > 0)
                            history.Last().Decision.Should().Be(ReviewDecision.Rejected, because);
                        break;
                }

                foreach (var r in history)
                    r.ReviewerId.Should().Be(r.Level == ReviewLevel.Manager ? e.Project.ManagerId : e.Project.DirectorId, because);

                history.Select(r => r.CreatedAt).Should().BeInAscendingOrder(because);
                history.Should().OnlyContain(r => r.CreatedAt <= DateTime.UtcNow, because);
            }
        }

        // ---------------------------------------------------------------- Every promised case is there

        [Fact]
        public async Task BudgetStates_CoverOnTrack_NearLimit_Exceeded_AndProjectedToExceed()
        {
            await using var s = _db.CreateServices();
            async Task<BLL.DTOs.Response.ProjectSummaryReport> Summary(string code, string director)
            {
                var id = await s.Db.Projects.Where(p => p.Code == code).Select(p => p.Id).SingleAsync();
                return await s.Reports.GetProjectSummaryAsync(_ids[director], AppRoles.Director, id);
            }

            var p1 = await Summary("PRJ-0001", "khaled");
            p1.BudgetStatus.Should().Be(BudgetStatus.OnTrack);
            p1.IsProjectedToExceed.Should().BeFalse();

            var p2 = await Summary("PRJ-0002", "khaled");
            p2.BudgetStatus.Should().Be(BudgetStatus.Exceeded);
            p2.ExceededBy.Should().Be(15);

            var p3 = await Summary("PRJ-0003", "khaled");
            p3.BudgetStatus.Should().Be(BudgetStatus.NearLimit);
            p3.UsagePercent.Should().Be(85);

            var p4 = await Summary("PRJ-0004", "khaled");
            p4.BudgetStatus.Should().Be(BudgetStatus.OnTrack);
            p4.IsProjectedToExceed.Should().BeTrue();
            p4.ProjectedRemaining.Should().Be(-5);

            var p8 = await Summary("PRJ-0008", "rania");
            p8.UsagePercent.Should().Be(100);

            var p9 = await Summary("PRJ-0009", "rania");
            p9.ActualHours.Should().Be(0);
        }

        [Fact]
        public async Task Portfolio_ShowsProblemsFirst()
        {
            await using var s = _db.CreateServices();

            var khaled = await s.Reports.GetPortfolioAsync(_ids["khaled"], new PortfolioFilterRequest());

            khaled.Items.Select(p => p.Code).Should().Equal("PRJ-0002", "PRJ-0004", "PRJ-0005", "PRJ-0003", "PRJ-0001", "PRJ-0006");
        }

        [Fact]
        public async Task ReviewQueues_ForEveryManagerAndDirector()
        {
            await using var s = _db.CreateServices();
            async Task<int> Queue(string user, string role)
                => (await s.Approvals.GetPendingAsync(_ids[user], role, new PagedRequest())).TotalCount;

            (await Queue("omar", AppRoles.Manager)).Should().Be(3);
            (await Queue("lina", AppRoles.Manager)).Should().Be(3);
            (await Queue("yousef", AppRoles.Manager)).Should().Be(5);
            (await Queue("hala", AppRoles.Manager)).Should().Be(0);
            (await Queue("tariq", AppRoles.Manager)).Should().Be(6);   // bulk approve
            (await Queue("rania", AppRoles.Director)).Should().Be(1);

            var khaled = await s.Approvals.GetPendingAsync(_ids["khaled"], AppRoles.Director, new PagedRequest());
            khaled.TotalCount.Should().Be(5);
            khaled.Items.Should().Contain(e => e.ProjectCode == "PRJ-0002" && e.BudgetImpact!.WouldExceedBudget);
        }

        [Fact]
        public async Task RejectionCases_ManagerRejected_DirectorRejected_Reopened_AndResubmitted()
        {
            await using var s = _db.CreateServices();
            async Task<List<BLL.DTOs.Response.TimeEntryResponse>> Entries(string employee, TimeEntryStatus status)
                => (await s.Entries.GetMyEntriesAsync(_ids[employee], new TimeEntryFilterRequest { Status = status })).Items;

            (await Entries("mohammad", TimeEntryStatus.Rejected)).Should().ContainSingle(e => e.RejectedAtLevel == ReviewLevel.Manager);
            (await Entries("dana", TimeEntryStatus.Rejected)).Should().ContainSingle(e => e.RejectedAtLevel == ReviewLevel.Director);

            var reopened = (await Entries("faris", TimeEntryStatus.Rejected)).Single();
            (await s.Entries.GetHistoryAsync(_ids["faris"], reopened.Id)).Select(h => h.Decision)
                .Should().Equal(ReviewDecision.Approved, ReviewDecision.Approved, ReviewDecision.Reopened);

            var resubmitted = (await Entries("zaid", TimeEntryStatus.PendingManager)).Single(e => e.ProjectCode == "PRJ-0007");
            resubmitted.RejectedAtLevel.Should().BeNull();
            (await s.Entries.GetHistoryAsync(_ids["zaid"], resubmitted.Id)).Should().ContainSingle(h => h.Decision == ReviewDecision.Rejected);

            (await Entries("sara", TimeEntryStatus.Cancelled)).Should().ContainSingle();
        }

        [Fact]
        public async Task BudgetLock_IsOnWhereHoursAreApproved_AndOffOnTheNewProject()
        {
            await using var s = _db.CreateServices();
            var ids = await s.Db.Projects.ToDictionaryAsync(p => p.Code, p => p.Id);

            (await s.Projects.GetByIdAsync(_ids["khaled"], AppRoles.Director, ids["PRJ-0001"])).IsBudgetLocked.Should().BeTrue();
            (await s.Projects.GetByIdAsync(_ids["rania"], AppRoles.Director, ids["PRJ-0009"])).IsBudgetLocked.Should().BeFalse();
        }

        [Fact]
        public async Task SeedingAgain_ChangesNothing()
        {
            await using (var before = _db.CreateContext())
            {
                var counts = (await before.Users.CountAsync(), await before.Projects.CountAsync(), await before.TimeEntries.CountAsync());

                await RunSeedAsync();

                await using var after = _db.CreateContext();
                (await after.Users.CountAsync(), await after.Projects.CountAsync(), await after.TimeEntries.CountAsync())
                    .Should().Be(counts);
            }
        }

        // ------------------------------------------------------------------

        private async Task RunSeedAsync()
        {
            await using var provider = BuildIdentity();
            using var scope = provider.CreateScope();
            var seed = scope.ServiceProvider.GetRequiredService<ISeedData>();
            await seed.IdentitySeedingAsync();
            await seed.DemoDataSeedingAsync(Today);
        }

        // Identity wired like Program.cs (same password rules), over this test's database.
        private ServiceProvider BuildIdentity()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<AppDbContext>(o => o.UseSqlServer(_db.ConnectionString));
            services.AddIdentityCore<ApplicationUser>(o =>
                {
                    o.Password.RequiredLength = 8;
                    o.Password.RequireNonAlphanumeric = false;
                    o.Password.RequireDigit = true;
                    o.Password.RequireLowercase = true;
                    o.Password.RequireUppercase = true;
                    o.User.RequireUniqueEmail = true;
                })
                .AddRoles<IdentityRole>()
                .AddEntityFrameworkStores<AppDbContext>();
            services.AddScoped<ISeedData, SeedData>();
            return services.BuildServiceProvider();
        }
    }
}
