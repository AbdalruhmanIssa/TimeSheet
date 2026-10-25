using TimeSheet.BLL.DTOs.Request;
using TimeSheet.BLL.DTOs.Response;

namespace TimeSheet.BLL.Services.Interface
{
    public interface IUserService
    {
        // Get
        Task<List<UserResponse>> GetAllAsync(UserFilterRequest filter);
        Task<UserResponse> GetByIdAsync(string userId);

        // Active Manager / Employee users for directors' pickers.
        Task<List<UserOptionResponse>> GetOptionsAsync(string role);

        // Create / update
        Task<UserResponse> CreateAsync(CreateUserRequest request);
        Task<UserResponse> UpdateAsync(string userId, UpdateUserRequest request);
        Task<UserResponse> ChangeRoleAsync(string currentUserId, string userId, ChangeUserRoleRequest request);
        Task ResetPasswordAsync(string userId, ResetUserPasswordRequest request);

        // Block / unblock
        Task<UserResponse> SetBlockedAsync(string currentUserId, string userId, bool isBlocked);

        // Delete
        Task DeleteAsync(string currentUserId, string userId);
    }
}
