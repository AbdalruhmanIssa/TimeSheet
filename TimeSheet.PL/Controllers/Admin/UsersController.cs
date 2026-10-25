using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TimeSheet.BLL.DTOs.Request;
using TimeSheet.BLL.Services.Interface;
using TimeSheet.DAL.Utilities;
using TimeSheet.PL.Extensions;

namespace TimeSheet.PL.Controllers.Admin
{
    [ApiController]
    [Area("Admin")]
    [Route("api/[area]/[controller]")]
    [Authorize(Roles = AppRoles.Admin)]
    public class UsersController : ControllerBase
    {
        private readonly IUserService _userService;

        public UsersController(IUserService userService)
        {
            _userService = userService;
        }

        // GET api/Admin/Users?role=Employee&isBlocked=false&search=sara
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] UserFilterRequest filter)
        {
            var users = await _userService.GetAllAsync(filter);
            return Ok(users);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(string id)
        {
            var user = await _userService.GetByIdAsync(id);
            return Ok(user);
        }

        [HttpPost]
        public async Task<IActionResult> Create(CreateUserRequest request)
        {
            var user = await _userService.CreateAsync(request);
            return CreatedAtAction(nameof(GetById), new { id = user.Id }, user);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, UpdateUserRequest request)
        {
            var user = await _userService.UpdateAsync(id, request);
            return Ok(user);
        }

        [HttpPatch("{id}/role")]
        public async Task<IActionResult> ChangeRole(string id, ChangeUserRoleRequest request)
        {
            var user = await _userService.ChangeRoleAsync(User.GetUserId(), id, request);
            return Ok(user);
        }

        [HttpPatch("{id}/password")]
        public async Task<IActionResult> ResetPassword(string id, ResetUserPasswordRequest request)
        {
            await _userService.ResetPasswordAsync(id, request);
            return Ok(new { success = true, message = "Password reset successfully." });
        }

        [HttpPatch("{id}/block")]
        public async Task<IActionResult> Block(string id)
        {
            var user = await _userService.SetBlockedAsync(User.GetUserId(), id, isBlocked: true);
            return Ok(user);
        }

        [HttpPatch("{id}/unblock")]
        public async Task<IActionResult> Unblock(string id)
        {
            var user = await _userService.SetBlockedAsync(User.GetUserId(), id, isBlocked: false);
            return Ok(user);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            await _userService.DeleteAsync(User.GetUserId(), id);
            return NoContent();
        }
    }
}
