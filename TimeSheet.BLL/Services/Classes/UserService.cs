using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TimeSheet.BLL.DTOs.Request;
using TimeSheet.BLL.DTOs.Response;
using TimeSheet.BLL.Exceptions;
using TimeSheet.BLL.Extensions;
using TimeSheet.BLL.Services.Interface;
using TimeSheet.DAL.Enums;
using TimeSheet.DAL.Models;
using TimeSheet.DAL.Repositories.Interface;
using TimeSheet.DAL.Utilities;

namespace TimeSheet.BLL.Services.Classes
{
    // Reads go through IUnitOfWork (so they can project + filter in SQL);
    // writes go through UserManager (password hashing, normalization, validation).
    public class UserService : IUserService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IUnitOfWork _uow;

        public UserService(UserManager<ApplicationUser> userManager, IUnitOfWork uow)
        {
            _userManager = userManager;
            _uow = uow;
        }

        public async Task<PagedResponse<UserResponse>> GetAllAsync(UserFilterRequest filter)
        {
            var query = ProjectUsers();

            if (!string.IsNullOrWhiteSpace(filter.Role))
            {
                var role = NormalizeRole(filter.Role);
                query = query.Where(u => u.Role == role);
            }

            if (filter.IsBlocked is { } isBlocked)
                query = query.Where(u => u.IsBlocked == isBlocked);

            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var search = filter.Search.Trim();
                query = query.Where(u =>
                    u.FullName.Contains(search) ||
                    u.UserName.Contains(search) ||
                    u.Email.Contains(search));
            }

            return await query.OrderBy(u => u.FullName).ThenBy(u => u.Id).ToPagedAsync(filter);
        }

        public async Task<UserResponse> GetByIdAsync(string userId)
        {
            return await ProjectUsers().FirstOrDefaultAsync(u => u.Id == userId)
                   ?? throw new NotFoundException("User not found.");
        }

        public async Task<UserResponse> CreateAsync(CreateUserRequest request)
        {
            var role = NormalizeRole(request.Role);

            // Create + role assignment are two saves; keep them atomic.
            var userId = await _uow.ExecuteInTransactionAsync(async () =>
            {
                var user = new ApplicationUser
                {
                    FullName = request.FullName.Trim(),
                    Email = request.Email.Trim(),
                    UserName = request.UserName.Trim(),
                    PhoneNumber = NullIfBlank(request.PhoneNumber),
                    EmailConfirmed = true // accounts are created by an Admin, no confirmation flow
                };

                EnsureSucceeded(await _userManager.CreateAsync(user, request.Password));
                EnsureSucceeded(await _userManager.AddToRoleAsync(user, role));
                return user.Id;
            });

            return await GetByIdAsync(userId);
        }

        public async Task<UserResponse> UpdateAsync(string userId, UpdateUserRequest request)
        {
            var user = await FindUserAsync(userId);

            user.FullName = request.FullName.Trim();
            user.Email = request.Email.Trim();
            user.UserName = request.UserName.Trim();
            user.PhoneNumber = NullIfBlank(request.PhoneNumber);

            // UpdateAsync re-validates uniqueness and re-normalizes email/username.
            EnsureSucceeded(await _userManager.UpdateAsync(user));

            return await GetByIdAsync(userId);
        }

        public async Task<UserResponse> ChangeRoleAsync(string currentUserId, string userId, ChangeUserRoleRequest request)
        {
            if (userId == currentUserId)
                throw new BadRequestException("You cannot change your own role.");

            var role = NormalizeRole(request.Role);
            var user = await FindUserAsync(userId);

            var currentRoles = await _userManager.GetRolesAsync(user);
            if (currentRoles.Count == 1 && currentRoles[0] == role)
                return await GetByIdAsync(userId);

            // A project's director/manager or an assigned employee would break the
            // project rules (e.g. a "Manager" assigned to log hours) if re-roled.
            await EnsureNotInUseOnProjectsAsync(userId);

            // One role per user: replace whatever is there.
            await _uow.ExecuteInTransactionAsync(async () =>
            {
                if (currentRoles.Count > 0)
                    EnsureSucceeded(await _userManager.RemoveFromRolesAsync(user, currentRoles));

                EnsureSucceeded(await _userManager.AddToRoleAsync(user, role));
                return true;
            });

            return await GetByIdAsync(userId);
        }

        public async Task<List<UserOptionResponse>> GetOptionsAsync(string role)
        {
            var normalized = NormalizeRole(role);
            if (normalized != AppRoles.Manager && normalized != AppRoles.Employee)
                throw new BadRequestException("Only Manager and Employee users can be listed here.");

            return await _uow.UsersInRole(normalized)
                .Where(u => !u.IsBlocked)
                .OrderBy(u => u.FullName)
                .Select(u => new UserOptionResponse
                {
                    Id = u.Id,
                    FullName = u.FullName,
                    UserName = u.UserName!
                })
                .ToListAsync();
        }

        public async Task ResetPasswordAsync(string userId, ResetUserPasswordRequest request)
        {
            var user = await FindUserAsync(userId);

            // Token route = password policy is enforced and the security stamp rotates.
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            EnsureSucceeded(await _userManager.ResetPasswordAsync(user, token, request.NewPassword));
        }

        public async Task<UserResponse> SetBlockedAsync(string currentUserId, string userId, bool isBlocked)
        {
            if (isBlocked && userId == currentUserId)
                throw new BadRequestException("You cannot block your own account.");

            var user = await FindUserAsync(userId);

            if (user.IsBlocked != isBlocked)
            {
                user.IsBlocked = isBlocked;
                EnsureSucceeded(await _userManager.UpdateAsync(user));
            }

            return await GetByIdAsync(userId);
        }

        public async Task DeleteAsync(string currentUserId, string userId)
        {
            if (userId == currentUserId)
                throw new BadRequestException("You cannot delete your own account.");

            var user = await FindUserAsync(userId);

            try
            {
                EnsureSucceeded(await _userManager.DeleteAsync(user));
            }
            catch (DbUpdateException)
            {
                // FKs to users are Restrict: projects / time entries keep the user alive.
                throw new BadRequestException(
                    "This user has related records (projects or time entries). Block the account instead.");
            }
        }

        // ------------------------------------------------------------------

        private IQueryable<UserResponse> ProjectUsers()
        {
            var userRoles = _uow.Repo<IdentityUserRole<string>>().Query();
            var roles = _uow.Repo<IdentityRole>().Query();

            return _uow.Repo<ApplicationUser>().Query()
                .Select(u => new UserResponse
                {
                    Id = u.Id,
                    FullName = u.FullName,
                    UserName = u.UserName!,
                    Email = u.Email!,
                    PhoneNumber = u.PhoneNumber,
                    IsBlocked = u.IsBlocked,
                    Role = userRoles
                        .Where(ur => ur.UserId == u.Id)
                        .Join(roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name)
                        .FirstOrDefault()
                });
        }

        private async Task EnsureNotInUseOnProjectsAsync(string userId)
        {
            var leadsProjects = await _uow.Repo<Project>().Query()
                .AnyAsync(p => p.Status != ProjectStatus.Archived && (p.DirectorId == userId || p.ManagerId == userId));

            var isAssigned = await _uow.Repo<ProjectAssignment>().Query()
                .AnyAsync(a => a.EmployeeId == userId && a.IsActive);

            if (leadsProjects || isAssigned)
                throw new BadRequestException(
                    "This user is the director or manager of a project, or is assigned to one. " +
                    "Reassign those projects (or archive them) before changing the role.");
        }

        private async Task<ApplicationUser> FindUserAsync(string userId)
        {
            return await _userManager.FindByIdAsync(userId)
                   ?? throw new NotFoundException("User not found.");
        }

        // Accepts any casing, returns the canonical role name.
        private static string NormalizeRole(string role)
        {
            return AppRoles.All.FirstOrDefault(r => string.Equals(r, role.Trim(), StringComparison.OrdinalIgnoreCase))
                   ?? throw new BadRequestException(
                       $"Unknown role '{role}'. Allowed roles: {string.Join(", ", AppRoles.All)}.");
        }

        private static void EnsureSucceeded(IdentityResult result)
        {
            if (!result.Succeeded)
                throw new BadRequestException(string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        private static string? NullIfBlank(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
