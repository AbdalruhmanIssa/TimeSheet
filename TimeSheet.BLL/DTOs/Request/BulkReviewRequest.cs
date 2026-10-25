using System.ComponentModel.DataAnnotations;

namespace TimeSheet.BLL.DTOs.Request
{
    public class BulkReviewRequest
    {
        // Each entry is approved on its own; one failure doesn't stop the others.
        [Required, MinLength(1), MaxLength(200)]
        public List<Guid> Ids { get; set; } = new();
    }
}
