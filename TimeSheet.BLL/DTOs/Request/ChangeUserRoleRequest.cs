using System.ComponentModel.DataAnnotations;

namespace TimeSheet.BLL.DTOs.Request
{
    public class ChangeUserRoleRequest
    {
        // Admin | Director | Manager | Employee
        [Required]
        public string Role { get; set; } = null!;
    }
}
