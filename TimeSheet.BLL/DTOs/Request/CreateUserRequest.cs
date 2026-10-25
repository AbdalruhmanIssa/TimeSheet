using System.ComponentModel.DataAnnotations;

namespace TimeSheet.BLL.DTOs.Request
{
    public class CreateUserRequest
    {
        [Required, MinLength(3), MaxLength(200)]
        public string FullName { get; set; } = null!;

        [Required, EmailAddress, MaxLength(256)]
        public string Email { get; set; } = null!;

        [Required, MinLength(3), MaxLength(50)]
        public string UserName { get; set; } = null!;

        [Phone, MaxLength(30)]
        public string? PhoneNumber { get; set; }

        // Initial password set by the Admin (Identity password policy applies).
        [Required]
        public string Password { get; set; } = null!;

        // Admin | Director | Manager | Employee
        [Required]
        public string Role { get; set; } = null!;
    }
}
