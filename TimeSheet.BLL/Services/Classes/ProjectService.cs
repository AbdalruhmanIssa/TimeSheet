using Mapster;
using Microsoft.EntityFrameworkCore;
using System.Data;
using TimeSheet.BLL.DTOs.Request;
using TimeSheet.BLL.DTOs.Response;
using TimeSheet.BLL.Exceptions;
using TimeSheet.BLL.Extensions;
using TimeSheet.BLL.Services.Interface;
using TimeSheet.DAL.Enums;
using TimeSheet.DAL.Models;
using TimeSheet.DAL.Repositories.Interface;
using TimeSheet.DAL.Utilities;

namespace TimeSheet.BLL.Services.Classes
{
    public class ProjectService : IProjectService
    {
        private const string CodePrefix = "PRJ-";

        private readonly IUnitOfWork _uow;
        private readonly IProjectAccessService _access;

        public ProjectService(IUnitOfWork uow, IProjectAccessService access)
        {
            _uow = uow;
            _access = access;
        }

        // ---------------------------------------------------------------- Reads

        public async Task<PagedResponse<ProjectResponse>> GetAllAsync(string userId, string role, ProjectFilterRequest filter)
        {
            var query = _access.VisibleProjects(userId, role);

            if (filter.Status is { } status)
                query = query.Where(p => p.Status == status);

            if (filter.ClientId is { } clientId)
                query = query.Where(p => p.ClientId == clientId);

            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var search = filter.Search.Trim();
                query = query.Where(p => p.Code.Contains(search) || p.Name.Contains(search));
            }

            return await query
                .OrderBy(p => p.Code) // unique
                .ProjectToType<ProjectResponse>()
                .ToPagedAsync(filter);
        }

        public async Task<ProjectDetailsResponse> GetByIdAsync(string userId, string role, Guid projectId)
        {
            var project = await _access.VisibleProjects(userId, role)
                              .Where(p => p.Id == projectId)
                              .ProjectToType<ProjectDetailsResponse>()
                              .FirstOrDefaultAsync()
                          ?? throw new NotFoundException("Project not found.");

            project.IsBudgetLocked = await HasApprovedEntriesAsync(projectId);
            project.Employees = project.Employees
                .OrderByDescending(e => e.IsActive)
                .ThenBy(e => e.FullName)
                .ToList();

            return project;
        }

        // ---------------------------------------------------------------- Create / update

        public async Task<ProjectDetailsResponse> CreateAsync(string directorId, CreateProjectRequest request)
        {
            var clientId = request.ClientId!.Value;
            var startDate = request.StartDate!.Value;

            EnsureValidDates(startDate, request.EndDate);
            await EnsureClientIsActiveAsync(clientId);
            await EnsureValidManagerAsync(request.ManagerId, directorId);

            var employeeIds = Distinct(request.EmployeeIds);
            await EnsureActiveEmployeesAsync(employeeIds);

            // Serializable: two directors creating at once must not get the same code.
            // (The unique index on Code is the safety net; a deadlock is retried by EF.)
            var name = request.Name.Trim();

            var projectId = await _uow.ExecuteInTransactionAsync(async () =>
            {
                // Inside the serializable transaction: two creates with the same name can't both pass.
                await EnsureNameIsFreeAsync(name, excludeProjectId: null);

                var project = new Project
                {
                    Code = await NextCodeAsync(),
                    Name = name,
                    Description = NullIfBlank(request.Description),
                    ClientId = clientId,
                    DirectorId = directorId,
                    ManagerId = request.ManagerId,
                    BudgetHours = request.BudgetHours,
                    StartDate = startDate,
                    EndDate = request.EndDate,
                    Status = ProjectStatus.Active,
                    Assignments = employeeIds
                        .Select(id => new ProjectAssignment { EmployeeId = id, IsActive = true })
                        .ToList()
                };

                await _uow.Repo<Project>().AddAsync(project);
                await _uow.SaveChangesAsync();
                return project.Id;
            }, IsolationLevel.Serializable);

            return await GetByIdAsync(directorId, AppRoles.Director, projectId);
        }

        public async Task<ProjectDetailsResponse> UpdateAsync(string directorId, Guid projectId, UpdateProjectRequest request)
        {
            var project = await _access.GetVisibleProjectAsync(projectId, directorId, AppRoles.Director);

            var clientId = request.ClientId!.Value;
            var startDate = request.StartDate!.Value;

            EnsureValidDates(startDate, request.EndDate);

            var name = request.Name.Trim();
            await EnsureNameIsFreeAsync(name, excludeProjectId: projectId);

            if (clientId != project.ClientId)
                await EnsureClientIsActiveAsync(clientId);

            if (request.ManagerId != project.ManagerId)
                await EnsureValidManagerAsync(request.ManagerId, directorId);

            // Budget is the contract with the client: once hours are approved against it,
            // it is fixed. A new deal = a new project.
            if (request.BudgetHours != project.BudgetHours && await HasApprovedEntriesAsync(projectId))
                throw new BadRequestException(
                    "The budget is locked because this project already has approved hours. Create a new project for a new deal.");

            project.Name = name;
            project.Description = NullIfBlank(request.Description);
            project.ClientId = clientId;
            project.ManagerId = request.ManagerId;
            project.BudgetHours = request.BudgetHours;
            project.StartDate = startDate;
            project.EndDate = request.EndDate;

            await _uow.SaveChangesAsync();

            return await GetByIdAsync(directorId, AppRoles.Director, projectId);
        }

        public async Task<ProjectDetailsResponse> ChangeStatusAsync(string directorId, Guid projectId, ChangeProjectStatusRequest request)
        {
            var status = request.Status!.Value;
            if (!Enum.IsDefined(status))
                throw new BadRequestException("Unknown project status.");

            var project = await _access.GetVisibleProjectAsync(projectId, directorId, AppRoles.Director);

            if (project.Status != status)
            {
                project.Status = status;
                await _uow.SaveChangesAsync();
            }

            return await GetByIdAsync(directorId, AppRoles.Director, projectId);
        }

        // ---------------------------------------------------------------- Assignments

        public async Task<ProjectDetailsResponse> AssignEmployeesAsync(string directorId, Guid projectId, AssignEmployeesRequest request)
        {
            // Scope check first (404 for someone else's project).
            await _access.GetVisibleProjectAsync(projectId, directorId, AppRoles.Director);

            var employeeIds = Distinct(request.EmployeeIds);
            if (employeeIds.Count == 0)
                throw new BadRequestException("At least one employee is required.");

            await EnsureActiveEmployeesAsync(employeeIds);

            var existing = await _uow.Repo<ProjectAssignment>().Query()
                .Where(a => a.ProjectId == projectId && employeeIds.Contains(a.EmployeeId))
                .ToListAsync();

            // Someone already on the team -> refuse the whole request (nothing is saved).
            var alreadyAssignedIds = existing.Where(a => a.IsActive).Select(a => a.EmployeeId).ToList();
            if (alreadyAssignedIds.Count > 0)
            {
                var names = await _uow.Repo<ApplicationUser>().Query()
                    .Where(u => alreadyAssignedIds.Contains(u.Id))
                    .OrderBy(u => u.FullName)
                    .Select(u => u.FullName)
                    .ToListAsync();

                throw new BadRequestException(
                    $"Already assigned to this project: {string.Join(", ", names)}. Remove them from the list and try again.");
            }

            foreach (var employeeId in employeeIds)
            {
                var removed = existing.FirstOrDefault(a => a.EmployeeId == employeeId);

                if (removed is null)
                    await _uow.Repo<ProjectAssignment>().AddAsync(new ProjectAssignment
                    {
                        ProjectId = projectId,
                        EmployeeId = employeeId,
                        IsActive = true
                    });
                else
                    removed.IsActive = true; // re-assigning a removed employee keeps the same row (unique index)
            }

            await _uow.SaveChangesAsync();

            return await GetByIdAsync(directorId, AppRoles.Director, projectId);
        }

        public async Task<ProjectDetailsResponse> RemoveEmployeeAsync(string directorId, Guid projectId, string employeeId)
        {
            await _access.GetVisibleProjectAsync(projectId, directorId, AppRoles.Director);

            var assignment = await _uow.Repo<ProjectAssignment>().Query()
                                 .FirstOrDefaultAsync(a => a.ProjectId == projectId && a.EmployeeId == employeeId && a.IsActive)
                             ?? throw new NotFoundException("This employee is not assigned to the project.");

            // Soft remove: the employee can't log new hours, history stays.
            assignment.IsActive = false;
            await _uow.SaveChangesAsync();

            return await GetByIdAsync(directorId, AppRoles.Director, projectId);
        }

        // ---------------------------------------------------------------- Rules

        private static void EnsureValidDates(DateOnly startDate, DateOnly? endDate)
        {
            if (endDate is { } end && end < startDate)
                throw new BadRequestException("End date cannot be before the start date.");
        }

        // Project names are unique across the system (any director, any status), so the
        // same project can't be created twice. The unique index on Name is the safety net.
        private async Task EnsureNameIsFreeAsync(string name, Guid? excludeProjectId)
        {
            var existingCode = await _uow.Repo<Project>().Query()
                .Where(p => p.Name == name && (excludeProjectId == null || p.Id != excludeProjectId))
                .Select(p => p.Code)
                .FirstOrDefaultAsync();

            if (existingCode is not null)
                throw new BadRequestException($"A project named '{name}' already exists ({existingCode}). Choose a different name.");
        }

        private async Task EnsureClientIsActiveAsync(Guid clientId)
        {
            var isActive = await _uow.Repo<Client>().Query()
                .AnyAsync(c => c.Id == clientId && c.IsActive);

            if (!isActive)
                throw new BadRequestException("Client not found or inactive.");
        }

        private async Task EnsureValidManagerAsync(string managerId, string directorId)
        {
            if (managerId == directorId)
                throw new BadRequestException("The manager cannot be the project's director.");

            var isManager = await _uow.UsersInRole(AppRoles.Manager)
                .AnyAsync(u => u.Id == managerId && !u.IsBlocked);

            if (!isManager)
                throw new BadRequestException("The selected manager is not an active user with the Manager role.");
        }

        // Only Employee users can be assigned, so managers/directors never log hours.
        private async Task EnsureActiveEmployeesAsync(List<string> employeeIds)
        {
            if (employeeIds.Count == 0)
                return;

            var valid = await _uow.UsersInRole(AppRoles.Employee)
                .Where(u => employeeIds.Contains(u.Id) && !u.IsBlocked)
                .Select(u => u.Id)
                .ToListAsync();

            var invalid = employeeIds.Except(valid).ToList();
            if (invalid.Count > 0)
                throw new BadRequestException(
                    $"Only active users with the Employee role can be assigned. Invalid: {string.Join(", ", invalid)}.");
        }

        private Task<bool> HasApprovedEntriesAsync(Guid projectId)
        {
            return _uow.Repo<TimeEntry>().Query()
                .AnyAsync(e => e.ProjectId == projectId && e.Status == TimeEntryStatus.Approved);
        }

        // PRJ-0001, PRJ-0002, ... PRJ-9999, PRJ-10000. Longest-then-highest code is the max,
        // so it keeps working past 4 digits.
        private async Task<string> NextCodeAsync()
        {
            var lastCode = await _uow.Repo<Project>().Query()
                .Where(p => p.Code.StartsWith(CodePrefix))
                .OrderByDescending(p => p.Code.Length)
                .ThenByDescending(p => p.Code)
                .Select(p => p.Code)
                .FirstOrDefaultAsync();

            var last = lastCode is not null && int.TryParse(lastCode[CodePrefix.Length..], out var n) ? n : 0;
            return $"{CodePrefix}{last + 1:D4}";
        }

        private static List<string> Distinct(IEnumerable<string>? ids)
            => ids?.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct().ToList() ?? new();

        private static string? NullIfBlank(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
