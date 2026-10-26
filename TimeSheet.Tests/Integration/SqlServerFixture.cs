using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TimeSheet.BLL.Services.Classes;
using TimeSheet.BLL.Settings;
using TimeSheet.DAL.Data;
using TimeSheet.DAL.Enums;
using TimeSheet.DAL.Models;
using TimeSheet.DAL.Repositories.Classes;
using TimeSheet.DAL.Utilities;

namespace TimeSheet.Tests.Integration
{
    // A real SQL Server database (LocalDB by default) built from the real migrations, so the
    // DB safety nets (unique/filtered index, check constraints, FK restrict, RowVersion) and
    // EF's SQL translation are tested for real. Created once per test run, dropped at the end.
    //
    // Override the server with TIMESHEET_TEST_SQLSERVER (a connection string without a database),
    // e.g. "Server=.;Trusted_Connection=True;TrustServerCertificate=True".
    // Run only unit tests with:  dotnet test --filter "Category!=Integration"
    public sealed class SqlServerFixture : IAsyncLifetime
    {
        // Business "today" for every integration test = 2026-10-26 (Asia/Riyadh).
        public static readonly DateTimeOffset UtcNow = new(2026, 10, 26, 9, 0, 0, TimeSpan.Zero);
        public static readonly DateOnly Today = new(2026, 10, 26);

        public string ConnectionString { get; }

        public SqlServerFixture()
        {
            var server = Environment.GetEnvironmentVariable("TIMESHEET_TEST_SQLSERVER")
                         ?? @"Server=(localdb)\MSSQLLocalDB;Integrated Security=True;TrustServerCertificate=True";

            ConnectionString = new SqlConnectionStringBuilder(server)
            {
                InitialCatalog = $"TimeSheet_IntegrationTests_{Guid.NewGuid():N}"
            }.ConnectionString;
        }

        public async Task InitializeAsync()
        {
            await using var db = CreateContext();
            await db.Database.MigrateAsync();

            db.Roles.AddRange(AppRoles.All.Select(r => new IdentityRole(r) { Id = RoleId(r), NormalizedName = r.ToUpperInvariant() }));
            await db.SaveChangesAsync();
        }

        public async Task DisposeAsync()
        {
            await using var db = CreateContext();
            await db.Database.EnsureDeletedAsync();
        }

        // Same provider options as Program.cs (retry on transient errors incl. deadlocks).
        public AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString, sql => sql.EnableRetryOnFailure(
                maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(1), errorNumbersToAdd: null))
            .Options);

        // A fresh set of services over a fresh DbContext — one per "request".
        public Services CreateServices() => new(CreateContext());

        // ---------------------------------------------------------------- Test data

        // Fresh users + client + two Active projects per test, so tests never share rows.
        public async Task<World> CreateWorldAsync(decimal budget = 100)
        {
            var tag = Guid.NewGuid().ToString("N")[..8];
            await using var db = CreateContext();

            string User(string kind, string role)
            {
                var id = $"it-{kind}-{tag}";
                db.Users.Add(new ApplicationUser
                {
                    Id = id,
                    UserName = id,
                    NormalizedUserName = id.ToUpperInvariant(),
                    Email = $"{id}@test.local",
                    NormalizedEmail = $"{id}@TEST.LOCAL",
                    FullName = $"{kind} {tag}",
                    SecurityStamp = Guid.NewGuid().ToString()
                });
                db.UserRoles.Add(new IdentityUserRole<string> { UserId = id, RoleId = RoleId(role) });
                return id;
            }

            var world = new World(
                DirectorId: User("director", AppRoles.Director),
                OtherDirectorId: User("director2", AppRoles.Director),
                ManagerId: User("manager", AppRoles.Manager),
                OtherManagerId: User("manager2", AppRoles.Manager),
                EmployeeId: User("employee", AppRoles.Employee),
                Employee2Id: User("employee2", AppRoles.Employee),
                ClientId: Guid.NewGuid(),
                ProjectId: Guid.NewGuid(),
                Project2Id: Guid.NewGuid());

            db.Clients.Add(new Client { Id = world.ClientId, Name = $"Client {tag}", IsActive = true });

            foreach (var (projectId, suffix) in new[] { (world.ProjectId, "A"), (world.Project2Id, "B") })
            {
                db.Projects.Add(new Project
                {
                    Id = projectId,
                    Code = $"IT-{tag}{suffix}",
                    Name = $"Project {suffix} {tag}",
                    ClientId = world.ClientId,
                    DirectorId = world.DirectorId,
                    ManagerId = world.ManagerId,
                    BudgetHours = budget,
                    StartDate = new DateOnly(2026, 9, 1),
                    Status = ProjectStatus.Active,
                    Assignments = new List<ProjectAssignment>
                    {
                        new() { EmployeeId = world.EmployeeId, IsActive = true },
                        new() { EmployeeId = world.Employee2Id, IsActive = true }
                    }
                });
            }

            await db.SaveChangesAsync();
            return world;
        }

        private static string RoleId(string role) => "role-" + role;
    }

    public sealed record World(
        string DirectorId,
        string OtherDirectorId,
        string ManagerId,
        string OtherManagerId,
        string EmployeeId,
        string Employee2Id,
        Guid ClientId,
        Guid ProjectId,
        Guid Project2Id);

    // The real BLL services wired like Program.cs, over one DbContext.
    public sealed class Services : IAsyncDisposable
    {
        public AppDbContext Db { get; }
        public UnitOfWork Uow { get; }
        public TimeEntryService Entries { get; }
        public ApprovalService Approvals { get; }
        public ProjectService Projects { get; }
        public ReportService Reports { get; }
        public DashboardService Dashboards { get; }

        public Services(AppDbContext db)
        {
            var settings = Options.Create(new TimeSheetSettings
            {
                BusinessTimeZone = "Asia/Riyadh",
                WeekStart = DayOfWeek.Sunday,
                MaxDailyHours = 12,
                MaxBackdateDays = 30
            });
            var clock = new BusinessClock(new FixedTimeProvider(SqlServerFixture.UtcNow), settings);

            Db = db;
            Uow = new UnitOfWork(db);
            var access = new ProjectAccessService(Uow);

            Entries = new TimeEntryService(Uow, access, clock, settings);
            Approvals = new ApprovalService(Uow, access);
            Projects = new ProjectService(Uow, access);
            Reports = new ReportService(Uow, access, clock);
            Dashboards = new DashboardService(Uow, access, Reports, clock, settings);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    [CollectionDefinition(Name)]
    public class SqlServerCollection : ICollectionFixture<SqlServerFixture>
    {
        public const string Name = "SqlServer";
    }
}
