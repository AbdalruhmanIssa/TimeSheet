using System.ComponentModel.DataAnnotations;

namespace TimeSheet.BLL.DTOs.Request
{
    public class CreateClientRequest
    {
        [Required, MaxLength(200)]
        public string Name { get; set; } = null!;

        [MaxLength(200)]
        public string? ContactName { get; set; }

        [EmailAddress, MaxLength(256)]
        public string? ContactEmail { get; set; }

        [Phone, MaxLength(30)]
        public string? Phone { get; set; }
    }
}
