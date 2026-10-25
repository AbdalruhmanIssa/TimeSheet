using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TimeSheet.BLL.DTOs.Request;
using TimeSheet.BLL.Services.Interface;

namespace TimeSheet.PL.Controllers.Identity
{
    [ApiController]
    [Area("Identity")]
    [Route("api/[area]/[controller]")]
    public class AuthsController : ControllerBase
    {
        private readonly IAuthenticationService _authService;

        public AuthsController(IAuthenticationService authService)
        {
            _authService = authService;
        }

        [AllowAnonymous]
        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginRequest request)
        {
            var result = await _authService.LoginAsync(request);
            return Ok(result);
        }
    }
}
