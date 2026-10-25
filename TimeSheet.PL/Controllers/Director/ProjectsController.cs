using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TimeSheet.BLL.DTOs.Request;
using TimeSheet.BLL.Services.Interface;
using TimeSheet.DAL.Utilities;
using TimeSheet.PL.Extensions;

namespace TimeSheet.PL.Controllers.Director
{
    [ApiController]
    [Area("Director")]
    [Route("api/[area]/[controller]")]
    [Authorize(Roles = AppRoles.Director)]
    public class ProjectsController : ControllerBase
    {
        private readonly IProjectService _projectService;

        public ProjectsController(IProjectService projectService)
        {
            _projectService = projectService;
        }

        // GET api/Director/Projects?status=Active&clientId=...&search=PRJ-00
        [HttpGet]
        public async Task<IActionResult> GetAll([FromQuery] ProjectFilterRequest filter)
        {
            var projects = await _projectService.GetAllAsync(User.GetUserId(), AppRoles.Director, filter);
            return Ok(projects);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var project = await _projectService.GetByIdAsync(User.GetUserId(), AppRoles.Director, id);
            return Ok(project);
        }

        [HttpPost]
        public async Task<IActionResult> Create(CreateProjectRequest request)
        {
            var project = await _projectService.CreateAsync(User.GetUserId(), request);
            return CreatedAtAction(nameof(GetById), new { id = project.Id }, project);
        }

        // Refuses a budget change once the project has approved entries.
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, UpdateProjectRequest request)
        {
            var project = await _projectService.UpdateAsync(User.GetUserId(), id, request);
            return Ok(project);
        }

        [HttpPatch("{id:guid}/status")]
        public async Task<IActionResult> ChangeStatus(Guid id, ChangeProjectStatusRequest request)
        {
            var project = await _projectService.ChangeStatusAsync(User.GetUserId(), id, request);
            return Ok(project);
        }

        [HttpPost("{id:guid}/employees")]
        public async Task<IActionResult> AssignEmployees(Guid id, AssignEmployeesRequest request)
        {
            var project = await _projectService.AssignEmployeesAsync(User.GetUserId(), id, request);
            return Ok(project);
        }

        [HttpDelete("{id:guid}/employees/{employeeId}")]
        public async Task<IActionResult> RemoveEmployee(Guid id, string employeeId)
        {
            var project = await _projectService.RemoveEmployeeAsync(User.GetUserId(), id, employeeId);
            return Ok(project);
        }
    }
}
