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
    public class DashboardsController : ControllerBase
    {
        private readonly IDashboardService _dashboardService;

        public DashboardsController(IDashboardService dashboardService)
        {
            _dashboardService = dashboardService;
        }

        // Hours today/week/month, pending and rejected counts. The frontend polls this.
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var dashboard = await _dashboardService.GetEmployeeDashboardAsync(User.GetUserId());
            return Ok(dashboard);
        }
    }
}
