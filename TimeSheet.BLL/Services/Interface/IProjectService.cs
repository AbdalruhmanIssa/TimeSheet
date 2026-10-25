using TimeSheet.BLL.DTOs.Request;
using TimeSheet.BLL.DTOs.Response;

namespace TimeSheet.BLL.Services.Interface
{
    public interface IProjectService
    {
        // Reads — scoped by role through IProjectAccessService.
        Task<List<ProjectResponse>> GetAllAsync(string userId, string role, ProjectFilterRequest filter);
        Task<ProjectDetailsResponse> GetByIdAsync(string userId, string role, Guid projectId);

        // Director writes — only on projects he directs.
        Task<ProjectDetailsResponse> CreateAsync(string directorId, CreateProjectRequest request);
        Task<ProjectDetailsResponse> UpdateAsync(string directorId, Guid projectId, UpdateProjectRequest request);
        Task<ProjectDetailsResponse> ChangeStatusAsync(string directorId, Guid projectId, ChangeProjectStatusRequest request);
        Task<ProjectDetailsResponse> AssignEmployeesAsync(string directorId, Guid projectId, AssignEmployeesRequest request);
        Task<ProjectDetailsResponse> RemoveEmployeeAsync(string directorId, Guid projectId, string employeeId);
    }
}
