using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TimeSheet.BLL.Services.Interface;
using TimeSheet.DAL.Utilities;

namespace TimeSheet.PL.Controllers.Director
{
    // Pickers for the project form (choose a manager / employees).
    [ApiController]
    [Area("Director")]
    [Route("api/[area]/[controller]")]
    [Authorize(Roles = AppRoles.Director)]
    public class UsersController : ControllerBase
    {
        private readonly IUserService _userService;

        public UsersController(IUserService userService)
        {
            _userService = userService;
        }

        // GET api/Director/Users?role=Manager | ?role=Employee
        [HttpGet]
        public async Task<IActionResult> GetOptions([FromQuery] string role)
        {
            var users = await _userService.GetOptionsAsync(role);
            return Ok(users);
        }
    }
}
