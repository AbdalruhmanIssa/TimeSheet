using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TimeSheet.BLL.DTOs.Request;
using TimeSheet.BLL.Services.Interface;
using TimeSheet.DAL.Utilities;
using TimeSheet.PL.Extensions;

namespace TimeSheet.PL.Controllers.Manager
{
    // Read-only: the projects this manager manages.
    [ApiController]
    [Area("Manager")]
    [Route("api/[area]/[controller]")]
    [Authorize(Roles = AppRoles.Manager)]
    public class ProjectsController : ControllerBase
    {
        private readonly IProjectService _projectService;

        public ProjectsController(IProjectService projectService)
        {
            _projectService = projectService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] ProjectFilterRequest filter)
        {
            var projects = await _projectService.GetAllAsync(User.GetUserId(), AppRoles.Manager, filter);
            return Ok(projects);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var project = await _projectService.GetByIdAsync(User.GetUserId(), AppRoles.Manager, id);
            return Ok(project);
        }
    }
}
