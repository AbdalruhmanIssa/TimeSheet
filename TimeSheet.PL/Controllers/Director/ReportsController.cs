using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TimeSheet.BLL.DTOs.Request;
using TimeSheet.BLL.Services.Interface;
using TimeSheet.DAL.Enums;
using TimeSheet.DAL.Utilities;
using TimeSheet.PL.Extensions;

namespace TimeSheet.PL.Controllers.Director
{
    // Reports on the projects I direct.
    [ApiController]
    [Area("Director")]
    [Route("api/[area]/[controller]")]
    [Authorize(Roles = AppRoles.Director)]
    public class ReportsController : ControllerBase
    {
        private readonly IReportService _reportService;
        private readonly IReportPdfService _reportPdfService;

        public ReportsController(IReportService reportService, IReportPdfService reportPdfService)
        {
            _reportService = reportService;
            _reportPdfService = reportPdfService;
        }

        // Same filters as /daily; downloads PRJ-0001_report_20261001-20261031.pdf
        [HttpGet("projects/{id:guid}/pdf")]
        public async Task<IActionResult> GetPdf(Guid id, [FromQuery] ReportFilterRequest filter)
        {
            var (content, fileName) = await _reportPdfService.GenerateProjectReportAsync(User.GetUserId(), AppRoles.Director, id, filter);
            return File(content, "application/pdf", fileName);
        }

        // All my projects with budget status — Exceeded / projected-to-exceed first.
        // GET api/Director/Reports/portfolio?status=Active&page=1&pageSize=20
        [HttpGet("portfolio")]
        public async Task<IActionResult> GetPortfolio([FromQuery] PortfolioFilterRequest filter)
        {
            var report = await _reportService.GetPortfolioAsync(User.GetUserId(), filter);
            return Ok(report);
        }

        [HttpGet("projects/{id:guid}/summary")]
        public async Task<IActionResult> GetSummary(Guid id)
        {
            var report = await _reportService.GetProjectSummaryAsync(User.GetUserId(), AppRoles.Director, id);
            return Ok(report);
        }

        // GET .../daily?from=2026-10-01&to=2026-10-31&groupBy=week
        [HttpGet("projects/{id:guid}/daily")]
        public async Task<IActionResult> GetDaily(Guid id, [FromQuery] ReportFilterRequest filter)
        {
            var report = await _reportService.GetDailyReportAsync(User.GetUserId(), AppRoles.Director, id, filter);
            return Ok(report);
        }

        // GET .../employees?from=2026-10-01&to=2026-10-31
        [HttpGet("projects/{id:guid}/employees")]
        public async Task<IActionResult> GetEmployees(Guid id, [FromQuery] ReportFilterRequest filter)
        {
            var report = await _reportService.GetEmployeeBreakdownAsync(User.GetUserId(), AppRoles.Director, id, filter);
            return Ok(report);
        }
    }
}
