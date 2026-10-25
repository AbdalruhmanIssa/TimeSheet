using System.ComponentModel.DataAnnotations;
using TimeSheet.DAL.Enums;

namespace TimeSheet.BLL.DTOs.Request
{
    public class ChangeProjectStatusRequest
    {
        // Active | OnHold | Completed | Archived
        [Required]
        public ProjectStatus? Status { get; set; }
    }
}
