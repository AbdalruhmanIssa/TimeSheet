namespace TimeSheet.DAL.Models
{
    // An Employee assigned to a project. Removing sets IsActive = false; history stays.
    public class ProjectAssignment : BaseEntity
    {
        public Guid ProjectId { get; set; }
        public Project Project { get; set; } = null!;

        public string EmployeeId { get; set; } = null!;
        public ApplicationUser Employee { get; set; } = null!;

        public bool IsActive { get; set; } = true;
    }
}
