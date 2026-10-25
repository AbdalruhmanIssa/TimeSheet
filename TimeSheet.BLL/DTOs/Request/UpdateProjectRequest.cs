using System.ComponentModel.DataAnnotations;

namespace TimeSheet.BLL.DTOs.Request
{
    public class UpdateProjectRequest
    {
        [Required, MaxLength(200)]
        public string Name { get; set; } = null!;

        [MaxLength(2000)]
        public string? Description { get; set; }

        [Required]
        public Guid? ClientId { get; set; }

        [Required]
        public string ManagerId { get; set; } = null!;

        // Refused if it changes once the project has Approved entries.
        [Range(typeof(decimal), "0.01", "9999999.99")]
        public decimal BudgetHours { get; set; }

        [Required]
        public DateOnly? StartDate { get; set; }

        public DateOnly? EndDate { get; set; }
    }
}
