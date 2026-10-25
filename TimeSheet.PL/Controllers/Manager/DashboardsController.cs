using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TimeSheet.BLL.Services.Interface;
using TimeSheet.DAL.Utilities;
using TimeSheet.PL.Extensions;

namespace TimeSheet.PL.Controllers.Manager
{
    [ApiController]
    [Area("Manager")]
    [Route("api/[area]/[controller]")]
    [Authorize(Roles = AppRoles.Manager)]
    public class DashboardsController : ControllerBase
    {
        private readonly IDashboardService _dashboardService;

        public DashboardsController(IDashboardService dashboardService)
        {
            _dashboardService = dashboardService;
        }

        // Pending review queue + budget alerts on my projects. The frontend polls this.
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var dashboard = await _dashboardService.GetManagerDashboardAsync(User.GetUserId());
            return Ok(dashboard);
        }
    }
}
