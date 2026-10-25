using System.ComponentModel.DataAnnotations;

namespace TimeSheet.BLL.DTOs.Request
{
    public class SubmitTimeEntryRequest
    {
        // From the Employee/Projects dropdown.
        [Required]
        public Guid? ProjectId { get; set; }

        // yyyy-MM-dd. Not in the future (business time zone), not older than MaxBackdateDays.
        [Required]
        public DateOnly? WorkDate { get; set; }

        // Per entry: > 0 and <= 12, at most 2 decimals. The daily cap across projects is checked separately.
        [Range(typeof(decimal), "0.01", "12")]
        public decimal Hours { get; set; }

        // Multiple tasks go in one entry's notes.
        [MaxLength(1000)]
        public string? Notes { get; set; }
    }
}
