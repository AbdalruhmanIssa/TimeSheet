using TimeSheet.BLL.DTOs.Response;

namespace TimeSheet.BLL.Services.Interface
{
    public interface IDashboardService
    {
        Task<EmployeeDashboardResponse> GetEmployeeDashboardAsync(string employeeId);
        Task<ManagerDashboardResponse> GetManagerDashboardAsync(string managerId);
        Task<DirectorDashboardResponse> GetDirectorDashboardAsync(string directorId);
    }
}
