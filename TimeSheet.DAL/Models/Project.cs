using TimeSheet.DAL.Enums;

namespace TimeSheet.DAL.Models
{
    public class Project : BaseEntity
    {
        // Auto-generated, e.g. "PRJ-0007".
        public string Code { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string? Description { get; set; }

        public Guid ClientId { get; set; }
        public Client Client { get; set; } = null!;

        public string DirectorId { get; set; } = null!;
        public ApplicationUser Director { get; set; } = null!;

        public string ManagerId { get; set; } = null!;
        public ApplicationUser Manager { get; set; } = null!;

        // Contracted hours. Locked once the project has its first Approved entry.
        // Actual hours are never stored — always SUM of Approved entries.
        public decimal BudgetHours { get; set; }

        public DateOnly StartDate { get; set; }
        public DateOnly? EndDate { get; set; }

        public ProjectStatus Status { get; set; } = ProjectStatus.Active;

        public ICollection<ProjectAssignment> Assignments { get; set; } = new List<ProjectAssignment>();
    }
}
