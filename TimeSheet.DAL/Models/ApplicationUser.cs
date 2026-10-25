using Microsoft.AspNetCore.Identity;

namespace TimeSheet.DAL.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string FullName { get; set; } = null!;
        public bool IsBlocked { get; set; } = false;
    }
}
