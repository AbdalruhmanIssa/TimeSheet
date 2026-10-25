using Microsoft.EntityFrameworkCore;
using TimeSheet.BLL.Exceptions;
using TimeSheet.BLL.Services.Interface;
using TimeSheet.DAL.Models;
using TimeSheet.DAL.Repositories.Interface;
using TimeSheet.DAL.Utilities;

namespace TimeSheet.BLL.Services.Classes
{
    public class ProjectAccessService : IProjectAccessService
    {
        private readonly IUnitOfWork _uow;

        public ProjectAccessService(IUnitOfWork uow)
        {
            _uow = uow;
        }

        public IQueryable<Project> VisibleProjects(string userId, string role)
        {
            var projects = _uow.Repo<Project>().Query();

            return role switch
            {
                AppRoles.Director => projects.Where(p => p.DirectorId == userId),
                AppRoles.Manager => projects.Where(p => p.ManagerId == userId),
                AppRoles.Employee => projects.Where(p => p.Assignments.Any(a => a.EmployeeId == userId && a.IsActive)),
                _ => projects.Where(p => false)
            };
        }

        public async Task<Project> GetVisibleProjectAsync(Guid projectId, string userId, string role)
        {
            return await VisibleProjects(userId, role).FirstOrDefaultAsync(p => p.Id == projectId)
                   ?? throw new NotFoundException("Project not found.");
        }
    }
}
