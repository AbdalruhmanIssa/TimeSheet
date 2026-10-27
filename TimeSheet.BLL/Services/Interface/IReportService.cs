using TimeSheet.BLL.DTOs.Request;
using TimeSheet.BLL.DTOs.Response;
using TimeSheet.DAL.Enums;

namespace TimeSheet.BLL.Services.Interface
{
    // Director + Manager reports, scoped to their own projects (out of scope -> 404).
    public interface IReportService
    {
        Task<ProjectSummaryReport> GetProjectSummaryAsync(string userId, string role, Guid projectId);

        // Opening balance + approved hours per day / week / month.
        Task<DailyReportResponse> GetDailyReportAsync(string userId, string role, Guid projectId, ReportFilterRequest filter);

        // Hours and share per employee.
        Task<List<EmployeeHoursRow>> GetEmployeeBreakdownAsync(string userId, string role, Guid projectId, ReportFilterRequest filter);

        // Director: all his projects, Exceeded / projected-to-exceed first.
        Task<PagedResponse<ProjectSummaryReport>> GetPortfolioAsync(string directorId, PortfolioFilterRequest filter);

        // Budget summaries of every project the user can see (any role), unordered.
        Task<List<ProjectSummaryReport>> GetProjectSummariesAsync(string userId, string role);
    }
}
