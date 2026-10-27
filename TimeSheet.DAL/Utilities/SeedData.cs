using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TimeSheet.DAL.Data;
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
            var userIds = await SeedDemoUsersAsync();
            await new DemoDataSeeder(_context, userIds).SeedAsync();
        }

        // Username = first name (quick to type in a demo). One password per role.
        public static readonly (string UserName, string FullName, string Phone, string Role)[] DemoUsers =
        {
            ("admin",    "Nadia Farouk",     "+966 50 100 0001", AppRoles.Admin),

            ("khaled",   "Khaled Mansour",   "+966 50 200 0001", AppRoles.Director),
            ("rania",    "Rania Saleh",      "+966 50 200 0002", AppRoles.Director),

            ("omar",     "Omar Haddad",      "+966 50 300 0001", AppRoles.Manager),
            ("lina",     "Lina Barakat",     "+966 50 300 0002", AppRoles.Manager),
            ("yousef",   "Yousef Nasser",    "+966 50 300 0003", AppRoles.Manager),
            ("hala",     "Hala Qasem",       "+966 50 300 0004", AppRoles.Manager),
            ("tariq",    "Tariq Aziz",       "+966 50 300 0005", AppRoles.Manager),

            ("ahmad",    "Ahmad Khalil",     "+966 50 400 0001", AppRoles.Employee),
            ("sara",     "Sara Odeh",        "+966 50 400 0002", AppRoles.Employee),
            ("mohammad", "Mohammad Darwish", "+966 50 400 0003", AppRoles.Employee),
            ("noor",     "Noor Hamdan",      "+966 50 400 0004", AppRoles.Employee),
            ("faris",    "Faris Jaber",      "+966 50 400 0005", AppRoles.Employee),
            ("dana",     "Dana Shami",       "+966 50 400 0006", AppRoles.Employee),
            ("bilal",    "Bilal Rahman",     "+966 50 400 0007", AppRoles.Employee),
            ("maya",     "Maya Issa",        "+966 50 400 0008", AppRoles.Employee),
            ("zaid",     "Zaid Masri",       "+966 50 400 0009", AppRoles.Employee),
            ("reem",     "Reem Salem",       "+966 50 400 0010", AppRoles.Employee),
        };

        public static string DemoPassword(string role) => $"{role}@1234";

        private async Task<Dictionary<string, string>> SeedDemoUsersAsync()
        {
            var ids = new Dictionary<string, string>();

            foreach (var (userName, fullName, phone, role) in DemoUsers)
            {
                var email = $"{fullName.ToLowerInvariant().Replace(' ', '.')}@timesheet.local";
                var user = await CreateUserIfNotExists(email, userName, fullName, phone, DemoPassword(role), role);
                ids[userName] = user.Id;
            }

            return ids;
        }

        // Skipped if a user with the same username OR email exists,
        // so editing one of those fields on a seeded user doesn't re-create it.
        private async Task<ApplicationUser> CreateUserIfNotExists(
            string email,
            string username,
            string fullName,
            string phone,
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
                PhoneNumber = phone,
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
