using System.ComponentModel.DataAnnotations;

namespace TimeSheet.BLL.DTOs.Request
{
    public class ResetUserPasswordRequest
    {
        [Required]
        public string NewPassword { get; set; } = null!;
    }
}
