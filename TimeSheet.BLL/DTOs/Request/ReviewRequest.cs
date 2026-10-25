using System.ComponentModel.DataAnnotations;

namespace TimeSheet.BLL.DTOs.Request
{
    // Body for reject and reopen — the comment is shown to the employee.
    public class ReviewRequest
    {
        [Required, MaxLength(1000)]
        public string Comment { get; set; } = null!;
    }
}
