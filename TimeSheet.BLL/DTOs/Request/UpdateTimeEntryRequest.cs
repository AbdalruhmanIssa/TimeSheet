using System.ComponentModel.DataAnnotations;

namespace TimeSheet.BLL.DTOs.Request
{
    // Allowed while PendingManager or Rejected. Editing a Rejected entry resubmits it (-> PendingManager).
    public class UpdateTimeEntryRequest
    {
        [Required]
        public Guid? ProjectId { get; set; }

        [Required]
        public DateOnly? WorkDate { get; set; }

        [Range(typeof(decimal), "0.01", "12")]
        public decimal Hours { get; set; }

        [MaxLength(1000)]
        public string? Notes { get; set; }
    }
}
