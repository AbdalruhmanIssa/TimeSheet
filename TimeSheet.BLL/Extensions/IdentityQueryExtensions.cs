using Microsoft.AspNetCore.Identity;
using TimeSheet.DAL.Models;
using TimeSheet.DAL.Repositories.Interface;

namespace TimeSheet.BLL.Extensions
{
    public static class IdentityQueryExtensions
    {
        // Users that have the given role (translates to SQL; no UserManager round-trips).
        public static IQueryable<ApplicationUser> UsersInRole(this IUnitOfWork uow, string role)
        {
            var roleIds = uow.Repo<IdentityRole>().Query()
                .Where(r => r.Name == role)
                .Select(r => r.Id);

            var userIds = uow.Repo<IdentityUserRole<string>>().Query()
                .Where(ur => roleIds.Contains(ur.RoleId))
                .Select(ur => ur.UserId);

            return uow.Repo<ApplicationUser>().Query()
                .Where(u => userIds.Contains(u.Id));
        }
    }
}
