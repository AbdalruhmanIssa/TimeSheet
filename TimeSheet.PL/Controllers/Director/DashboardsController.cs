using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TimeSheet.BLL.Services.Interface;
using TimeSheet.DAL.Utilities;
using TimeSheet.PL.Extensions;

namespace TimeSheet.PL.Controllers.Director
{
    [ApiController]
    [Area("Director")]
    [Route("api/[area]/[controller]")]
    [Authorize(Roles = AppRoles.Director)]
    public class DashboardsController : ControllerBase
    {
        private readonly IDashboardService _dashboardService;

        public DashboardsController(IDashboardService dashboardService)
        {
            _dashboardService = dashboardService;
        }

        // Pending final approvals, budget alerts, approved hours this month. The frontend polls this.
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var dashboard = await _dashboardService.GetDirectorDashboardAsync(User.GetUserId());
            return Ok(dashboard);
        }
    }
}
