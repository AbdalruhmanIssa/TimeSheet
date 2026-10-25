using Mapster;
using Microsoft.AspNetCore.Identity;
using MockQueryable;
using Moq;
using System.Data;
using System.Runtime.CompilerServices;
using TimeSheet.BLL.Mapping;
using TimeSheet.DAL.Models;
using TimeSheet.DAL.Repositories.Interface;
using TimeSheet.DAL.Utilities;

namespace TimeSheet.Tests
{
    internal static class TestHelpers
    {
        // Same Mapster registrations as Program.cs, once for the whole test assembly.
        [ModuleInitializer]
        internal static void RegisterMapster()
            => TypeAdapterConfig.GlobalSettings.Scan(typeof(MapsterConfig).Assembly);

        // UserManager has an ugly constructor; only the store is needed for mocking.
        public static Mock<UserManager<ApplicationUser>> MockUserManager()
        {
            var store = new Mock<IUserStore<ApplicationUser>>();
            return new Mock<UserManager<ApplicationUser>>(
                store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
        }

        // Makes uow.Repo<T>().Query() return the given in-memory list (async-capable).
        public static Mock<IGenericRepository<T>> SetupRepo<T>(this Mock<IUnitOfWork> uow, List<T> data) where T : class
        {
            var repo = new Mock<IGenericRepository<T>>();
            repo.Setup(r => r.Query()).Returns(data.BuildMock());
            uow.Setup(u => u.Repo<T>()).Returns(repo.Object);
            return repo;
        }

        // Runs the operation passed to ExecuteInTransactionAsync directly (both overloads).
        public static void SetupTransaction<T>(this Mock<IUnitOfWork> uow)
        {
            uow.Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<Task<T>>>(), It.IsAny<CancellationToken>()))
               .Returns((Func<Task<T>> operation, CancellationToken _) => operation());

            uow.Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<Task<T>>>(), It.IsAny<IsolationLevel>(), It.IsAny<CancellationToken>()))
               .Returns((Func<Task<T>> operation, IsolationLevel _, CancellationToken _) => operation());
        }

        // Users + roles + user-role links, the way the services read them.
        public static IdentityData SetupIdentityData(this Mock<IUnitOfWork> uow, params (ApplicationUser User, string Role)[] users)
        {
            var data = new IdentityData(
                users.Select(x => x.User).ToList(),
                AppRoles.All.Select(r => new IdentityRole(r) { Id = RoleId(r) }).ToList(),
                users.Select(x => new IdentityUserRole<string> { UserId = x.User.Id, RoleId = RoleId(x.Role) }).ToList());

            uow.SetupRepo(data.Users);
            uow.SetupRepo(data.Roles);
            uow.SetupRepo(data.UserRoles);
            return data;
        }

        public static string RoleId(string role) => "role-" + role;

        public static ApplicationUser NewUser(string id, string fullName = "Test User", bool isBlocked = false) => new()
        {
            Id = id,
            FullName = fullName,
            UserName = id,
            Email = id + "@test.local",
            IsBlocked = isBlocked
        };
    }

    // A clock that always returns the same instant.
    internal sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        public FixedTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    internal record IdentityData(
        List<ApplicationUser> Users,
        List<IdentityRole> Roles,
        List<IdentityUserRole<string>> UserRoles);
}
