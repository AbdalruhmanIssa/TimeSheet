using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TimeSheet.BLL.Services.Interface;
using TimeSheet.DAL.Utilities;
using TimeSheet.PL.Extensions;

namespace TimeSheet.PL.Controllers.Employee
{
    [ApiController]
    [Area("Employee")]
    [Route("api/[area]/[controller]")]
    [Authorize(Roles = AppRoles.Employee)]
    public class ProjectsController : ControllerBase
    {
        private readonly ITimeEntryService _timeEntryService;

        public ProjectsController(ITimeEntryService timeEntryService)
        {
            _timeEntryService = timeEntryService;
        }

        // Dropdown for logging hours: active projects I'm actively assigned to (id, code, name).
        [HttpGet]
        public async Task<IActionResult> GetOptions()
        {
            var projects = await _timeEntryService.GetProjectOptionsAsync(User.GetUserId());
            return Ok(projects);
        }
    }
}
