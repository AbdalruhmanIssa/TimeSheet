using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TimeSheet.DAL.Data;
using TimeSheet.DAL.Enums;
using TimeSheet.DAL.Models;

namespace TimeSheet.DAL.Utilities
{
    public class SeedData : ISeedData
    {
        private readonly AppDbContext _context;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly UserManager<ApplicationUser> _userManager;

        public SeedData(
            AppDbContext context,
            RoleManager<IdentityRole> roleManager,
            UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _roleManager = roleManager;
            _userManager = userManager;
        }

        // Apply pending migrations automatically
        public async Task DataSeedingAsync()
        {
            if ((await _context.Database.GetPendingMigrationsAsync()).Any())
            {
                await _context.Database.MigrateAsync();
            }
        }

        // Seed roles (every environment)
        public async Task IdentitySeedingAsync()
        {
            foreach (var role in AppRoles.All)
            {
                if (!await _roleManager.RoleExistsAsync(role))
                    await _roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        // Seed demo users + demo data (Development only — see Program.cs)
        public async Task DemoDataSeedingAsync()
        {
            await SeedDemoUsersAsync();
            await SeedDemoClientsAsync();
            await SeedDemoProjectsAsync();
            await SeedDemoTimeEntriesAsync();
            await SeedDemoReviewedEntriesAsync();
        }

        private async Task SeedDemoUsersAsync()
        {
            // Each user is skipped if a user with the same username OR email exists,
            // so editing one of those fields on a seeded user doesn't re-create it.
            await CreateUserIfNotExists("admin@timesheet.local", "admin", "Demo Admin", "Admin@1234", AppRoles.Admin);

            await CreateUserIfNotExists("director@timesheet.local", "director", "Demo Director", "Director@1234", AppRoles.Director);
            await CreateUserIfNotExists("director2@timesheet.local", "director2", "Demo Director Two", "Director@1234", AppRoles.Director);

            await CreateUserIfNotExists("manager@timesheet.local", "manager", "Demo Manager", "Manager@1234", AppRoles.Manager);
            await CreateUserIfNotExists("manager2@timesheet.local", "manager2", "Demo Manager Two", "Manager@1234", AppRoles.Manager);

            await CreateUserIfNotExists("employee@timesheet.local", "employee", "Demo Employee", "Employee@1234", AppRoles.Employee);
            await CreateUserIfNotExists("employee2@timesheet.local", "employee2", "Demo Employee Two", "Employee@1234", AppRoles.Employee);
            await CreateUserIfNotExists("employee3@timesheet.local", "employee3", "Demo Employee Three", "Employee@1234", AppRoles.Employee);
        }

        private async Task SeedDemoClientsAsync()
        {
            // Only when the table is empty, so edits/deactivations made while testing stick.
            if (await _context.Clients.AnyAsync())
                return;

            _context.Clients.AddRange(
                new Client
                {
                    Name = "Acme Trading Co.",
                    ContactName = "Sara Haddad",
                    ContactEmail = "sara.haddad@acme.example",
                    Phone = "+966 11 000 0001"
                },
                new Client
                {
                    Name = "Northwind Logistics",
                    ContactName = "Omar Khalil",
                    ContactEmail = "omar.khalil@northwind.example",
                    Phone = "+966 11 000 0002"
                },
                new Client
                {
                    Name = "Contoso Health",
                    ContactName = "Lina Mansour",
                    ContactEmail = "lina.mansour@contoso.example"
                },
                new Client
                {
                    Name = "Fabrikam Construction",
                    ContactName = "Yousef Nasser",
                    IsActive = false
                });

            await _context.SaveChangesAsync();
        }

        private async Task SeedDemoProjectsAsync()
        {
            // Only when the table is empty, so changes made while testing stick.
            if (await _context.Projects.AnyAsync())
                return;

            var users = await _context.Users
                .Where(u => u.UserName != null)
                .ToDictionaryAsync(u => u.UserName!, u => u.Id);
            var clients = await _context.Clients.ToDictionaryAsync(c => c.Name, c => c.Id);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            var demo = new[]
            {
                new { Code = "PRJ-0001", Name = "Acme ERP Rollout", Client = "Acme Trading Co.",
                      Director = "director", Manager = "manager", Budget = 500m,
                      Start = today.AddDays(-60), End = (DateOnly?)today.AddDays(120), Status = ProjectStatus.Active,
                      Employees = new[] { "employee", "employee2" } },
                new { Code = "PRJ-0002", Name = "Northwind Warehouse App", Client = "Northwind Logistics",
                      Director = "director", Manager = "manager2", Budget = 300m,
                      Start = today.AddDays(-30), End = (DateOnly?)null, Status = ProjectStatus.Active,
                      Employees = new[] { "employee2", "employee3" } },
                new { Code = "PRJ-0003", Name = "Contoso Patient Portal", Client = "Contoso Health",
                      Director = "director2", Manager = "manager", Budget = 200m,
                      Start = today.AddDays(-90), End = (DateOnly?)today.AddDays(30), Status = ProjectStatus.OnHold,
                      Employees = new[] { "employee3" } }
            };

            foreach (var p in demo)
            {
                // Skip a project if a seeded user/client it needs was renamed or removed.
                if (!clients.TryGetValue(p.Client, out var clientId) ||
                    !users.TryGetValue(p.Director, out var directorId) ||
                    !users.TryGetValue(p.Manager, out var managerId) ||
                    p.Employees.Any(e => !users.ContainsKey(e)))
                    continue;

                _context.Projects.Add(new Project
                {
                    Code = p.Code,
                    Name = p.Name,
                    Description = $"Demo project for {p.Client}.",
                    ClientId = clientId,
                    DirectorId = directorId,
                    ManagerId = managerId,
                    BudgetHours = p.Budget,
                    StartDate = p.Start,
                    EndDate = p.End,
                    Status = p.Status,
                    Assignments = p.Employees
                        .Select(e => new ProjectAssignment { EmployeeId = users[e] })
                        .ToList()
                });
            }

            await _context.SaveChangesAsync();
        }

        private async Task SeedDemoTimeEntriesAsync()
        {
            // Only when the table is empty. All entries start as PendingManager (waiting for review),
            // stay within the daily cap and the backdate window, one per project per day.
            if (await _context.TimeEntries.AnyAsync())
                return;

            var users = await _context.Users
                .Where(u => u.UserName != null)
                .ToDictionaryAsync(u => u.UserName!, u => u.Id);
            var projects = await _context.Projects.ToDictionaryAsync(p => p.Code, p => p.Id);

            // UTC "today" is never ahead of the business time zone (Asia/Riyadh), so these are never in the future.
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var now = DateTime.UtcNow;

            var demo = new (string Employee, string Project, int DaysAgo, decimal Hours, string Notes)[]
            {
                ("employee",  "PRJ-0001", 1, 8m,   "Requirements workshop with Acme finance team"),
                ("employee",  "PRJ-0001", 2, 6.5m, "Configured chart of accounts; data mapping"),
                ("employee",  "PRJ-0001", 3, 4m,   "Kick-off meeting and environment setup"),
                ("employee2", "PRJ-0001", 1, 5m,   "Inventory module setup"),
                ("employee2", "PRJ-0002", 1, 6m,   "Warehouse app: barcode scanning screen"),
                ("employee2", "PRJ-0002", 2, 7.5m, "API endpoints for stock movements"),
                ("employee3", "PRJ-0002", 1, 8m,   "UI review and bug fixes")
            };

            foreach (var e in demo)
            {
                if (!users.TryGetValue(e.Employee, out var employeeId) ||
                    !projects.TryGetValue(e.Project, out var projectId))
                    continue;

                _context.TimeEntries.Add(new TimeEntry
                {
                    ProjectId = projectId,
                    EmployeeId = employeeId,
                    WorkDate = today.AddDays(-e.DaysAgo),
                    Hours = e.Hours,
                    Notes = e.Notes,
                    Status = TimeEntryStatus.PendingManager,
                    SubmittedAt = now
                });
            }

            await _context.SaveChangesAsync();
        }

        // Entries that already went through review, so every status and the history show up.
        // Runs whenever there are no reviews yet (also on a DB seeded before reviews existed);
        // an entry is skipped if that employee already has an entry for that project and day.
        private async Task SeedDemoReviewedEntriesAsync()
        {
            if (await _context.TimeEntryReviews.AnyAsync())
                return;

            var users = await _context.Users
                .Where(u => u.UserName != null)
                .ToDictionaryAsync(u => u.UserName!, u => u.Id);
            var projects = await _context.Projects
                .ToDictionaryAsync(p => p.Code, p => new { p.Id, p.ManagerId, p.DirectorId });

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var now = DateTime.UtcNow;

            var demo = new (string Employee, string Project, int DaysAgo, decimal Hours, string Notes,
                TimeEntryStatus Status, (ReviewLevel Level, ReviewDecision Decision, string? Comment)[] Reviews)[]
            {
                ("employee", "PRJ-0001", 5, 7.5m, "Gap analysis for purchasing workflow",
                    TimeEntryStatus.Approved,
                    new[] { (ReviewLevel.Manager, ReviewDecision.Approved, (string?)null),
                            (ReviewLevel.Director, ReviewDecision.Approved, (string?)null) }),

                ("employee2", "PRJ-0002", 4, 6m, "Misc work",
                    TimeEntryStatus.Rejected,
                    new[] { (ReviewLevel.Manager, ReviewDecision.Rejected,
                             (string?)"Please list the tasks you worked on in the notes.") }),

                ("employee3", "PRJ-0002", 3, 5m, "Stock transfer screen + unit tests",
                    TimeEntryStatus.PendingDirector,
                    new[] { (ReviewLevel.Manager, ReviewDecision.Approved, (string?)null) })
            };

            foreach (var e in demo)
            {
                if (!users.TryGetValue(e.Employee, out var employeeId) ||
                    !projects.TryGetValue(e.Project, out var project))
                    continue;

                var workDate = today.AddDays(-e.DaysAgo);
                var dayTaken = await _context.TimeEntries.AnyAsync(t =>
                    t.EmployeeId == employeeId && t.ProjectId == project.Id &&
                    t.WorkDate == workDate && t.Status != TimeEntryStatus.Cancelled);
                if (dayTaken)
                    continue;

                _context.TimeEntries.Add(new TimeEntry
                {
                    ProjectId = project.Id,
                    EmployeeId = employeeId,
                    WorkDate = workDate,
                    Hours = e.Hours,
                    Notes = e.Notes,
                    Status = e.Status,
                    SubmittedAt = now,
                    Reviews = e.Reviews.Select(r => new TimeEntryReview
                    {
                        ReviewerId = r.Level == ReviewLevel.Manager ? project.ManagerId : project.DirectorId,
                        Level = r.Level,
                        Decision = r.Decision,
                        Comment = r.Comment
                    }).ToList()
                });
            }

            await _context.SaveChangesAsync();
        }

        private async Task<ApplicationUser> CreateUserIfNotExists(
            string email,
            string username,
            string fullName,
            string password,
            string role)
        {
            var user = await _userManager.FindByNameAsync(username)
                       ?? await _userManager.FindByEmailAsync(email);
            if (user != null)
                return user;

            user = new ApplicationUser
            {
                FullName = fullName,
                Email = email,
                UserName = username,
                EmailConfirmed = true,
                IsBlocked = false
            };

            var result = await _userManager.CreateAsync(user, password);

            if (!result.Succeeded)
                throw new Exception(string.Join(";", result.Errors.Select(e => e.Description)));

            await _userManager.AddToRoleAsync(user, role);
            return user;
        }
    }
}
