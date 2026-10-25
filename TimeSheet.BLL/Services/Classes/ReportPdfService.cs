using Microsoft.EntityFrameworkCore;
using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using TimeSheet.BLL.Documents;
using TimeSheet.BLL.DTOs.Request;
using TimeSheet.BLL.Services.Interface;
using TimeSheet.DAL.Models;
using TimeSheet.DAL.Repositories.Interface;

namespace TimeSheet.BLL.Services.Classes
{
    public class ReportPdfService : IReportPdfService
    {
        private readonly IReportService _reports;
        private readonly IUnitOfWork _uow;
        private readonly IBusinessClock _clock;

        static ReportPdfService()
        {
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public ReportPdfService(IReportService reports, IUnitOfWork uow, IBusinessClock clock)
        {
            _reports = reports;
            _uow = uow;
            _clock = clock;
        }

        public async Task<(byte[] Content, string FileName)> GenerateProjectReportAsync(
            string userId, string role, Guid projectId, ReportFilterRequest filter)
        {
            // The JSON reports do the scoping (404) and validation (400); the PDF just lays them out.
            var summary = await _reports.GetProjectSummaryAsync(userId, role, projectId);
            var daily = await _reports.GetDailyReportAsync(userId, role, projectId, filter);

            // Per-employee table covers the same period as the daily table.
            var employees = await _reports.GetEmployeeBreakdownAsync(userId, role, projectId,
                new ReportFilterRequest { From = daily.From, To = daily.To });

            var generatedBy = await _uow.Repo<ApplicationUser>().Query()
                                  .Where(u => u.Id == userId)
                                  .Select(u => u.FullName)
                                  .FirstOrDefaultAsync()
                              ?? userId;

            var document = new ProjectReportDocument(new ProjectReportData
            {
                Summary = summary,
                Daily = daily,
                Employees = employees,
                GeneratedAt = _clock.LocalNow,
                GeneratedBy = generatedBy
            });

            var fileName = $"{summary.Code}_report_{daily.From:yyyyMMdd}-{daily.To:yyyyMMdd}.pdf";
            return (Render(document), fileName);
        }

        // Dates and numbers in the PDF ("26 Sep 2026", "1,234.5 h") must not depend on the
        // server's culture. Layout runs synchronously inside GeneratePdf, and CurrentCulture is
        // per execution context, so this only affects this request.
        private static byte[] Render(IDocument document)
        {
            var previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            try
            {
                return document.GeneratePdf();
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }
    }
}
