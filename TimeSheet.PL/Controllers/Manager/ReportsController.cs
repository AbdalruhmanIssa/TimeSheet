using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TimeSheet.BLL.DTOs.Request;
using TimeSheet.BLL.Services.Interface;
using TimeSheet.DAL.Utilities;
using TimeSheet.PL.Extensions;

namespace TimeSheet.PL.Controllers.Manager
{
    // Reports on the projects I manage.
    [ApiController]
    [Area("Manager")]
    [Route("api/[area]/[controller]")]
    [Authorize(Roles = AppRoles.Manager)]
    public class ReportsController : ControllerBase
    {
        private readonly IReportService _reportService;
        private readonly IReportPdfService _reportPdfService;

        public ReportsController(IReportService reportService, IReportPdfService reportPdfService)
        {
            _reportService = reportService;
            _reportPdfService = reportPdfService;
        }

        // Same filters as /daily.
        [HttpGet("projects/{id:guid}/pdf")]
        public async Task<IActionResult> GetPdf(Guid id, [FromQuery] ReportFilterRequest filter)
        {
            var (content, fileName) = await _reportPdfService.GenerateProjectReportAsync(User.GetUserId(), AppRoles.Manager, id, filter);
            return File(content, "application/pdf", fileName);
        }

        [HttpGet("projects/{id:guid}/summary")]
        public async Task<IActionResult> GetSummary(Guid id)
        {
            var report = await _reportService.GetProjectSummaryAsync(User.GetUserId(), AppRoles.Manager, id);
            return Ok(report);
        }

        [HttpGet("projects/{id:guid}/daily")]
        public async Task<IActionResult> GetDaily(Guid id, [FromQuery] ReportFilterRequest filter)
        {
            var report = await _reportService.GetDailyReportAsync(User.GetUserId(), AppRoles.Manager, id, filter);
            return Ok(report);
        }

        [HttpGet("projects/{id:guid}/employees")]
        public async Task<IActionResult> GetEmployees(Guid id, [FromQuery] ReportFilterRequest filter)
        {
            var report = await _reportService.GetEmployeeBreakdownAsync(User.GetUserId(), AppRoles.Manager, id, filter);
            return Ok(report);
        }
    }
}
