using TimeSheet.BLL.DTOs.Request;
using TimeSheet.BLL.DTOs.Response;

namespace TimeSheet.BLL.Services.Interface
{
    public interface IAuthenticationService
    {
        Task<LoginResponse> LoginAsync(LoginRequest request);

        // Used on every authenticated request: the token's user must still exist,
        // not be blocked, and still have the role the token was issued with.
        Task<bool> IsUserActiveAsync(string userId, string? tokenRole);
    }
}
