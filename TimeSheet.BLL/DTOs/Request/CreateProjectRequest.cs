using System.ComponentModel.DataAnnotations;

namespace TimeSheet.BLL.DTOs.Request
{
    public class CreateProjectRequest
    {
        [Required, MaxLength(200)]
        public string Name { get; set; } = null!;

        [MaxLength(2000)]
        public string? Description { get; set; }

        [Required]
        public Guid? ClientId { get; set; }

        // Must be a user with the Manager role.
        [Required]
        public string ManagerId { get; set; } = null!;

        [Range(typeof(decimal), "0.01", "9999999.99")]
        public decimal BudgetHours { get; set; }

        [Required]
        public DateOnly? StartDate { get; set; }

        public DateOnly? EndDate { get; set; }

        // Optional initial team; each must be a user with the Employee role.
        public List<string>? EmployeeIds { get; set; }
    }
}
