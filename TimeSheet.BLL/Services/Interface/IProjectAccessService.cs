using TimeSheet.DAL.Models;

namespace TimeSheet.BLL.Services.Interface
{
    // The ONE place that decides which projects a user can see (DESIGN §3 rule 11).
    // Out-of-scope projects behave as if they don't exist (404).
    public interface IProjectAccessService
    {
        // Director: projects he directs. Manager: projects he manages.
        // Employee: projects with an active assignment. Anyone else: none.
        IQueryable<Project> VisibleProjects(string userId, string role);

        // Tracked entity, or NotFoundException("Project not found.").
        Task<Project> GetVisibleProjectAsync(Guid projectId, string userId, string role);
    }
}
