using System.ComponentModel.DataAnnotations;

namespace TimeSheet.BLL.DTOs.Request
{
    public class LoginRequest
    {
        [Required]
        public string EmailOrUserName { get; set; } = null!;
        [Required]
        public string Password { get; set; } = null!;
    }
}
