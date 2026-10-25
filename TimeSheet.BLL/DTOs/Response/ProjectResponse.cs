using TimeSheet.DAL.Enums;

namespace TimeSheet.BLL.DTOs.Response
{
    public class ProjectResponse
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string? Description { get; set; }

        public Guid ClientId { get; set; }
        public string ClientName { get; set; } = null!;

        public string DirectorId { get; set; } = null!;
        public string DirectorName { get; set; } = null!;

        public string ManagerId { get; set; } = null!;
        public string ManagerName { get; set; } = null!;

        public decimal BudgetHours { get; set; }
        public DateOnly StartDate { get; set; }
        public DateOnly? EndDate { get; set; }
        public ProjectStatus Status { get; set; }

        public int ActiveEmployeeCount { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
