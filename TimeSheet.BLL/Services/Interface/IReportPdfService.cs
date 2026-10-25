using TimeSheet.BLL.DTOs.Request;

namespace TimeSheet.BLL.Services.Interface
{
    public interface IReportPdfService
    {
        // Project report as PDF: budget summary + hours by day/week/month + per employee.
        // Same scoping as the JSON reports (out of scope -> 404).
        Task<(byte[] Content, string FileName)> GenerateProjectReportAsync(
            string userId, string role, Guid projectId, ReportFilterRequest filter);
    }
}
