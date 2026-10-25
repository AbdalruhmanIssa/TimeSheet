namespace TimeSheet.BLL.DTOs.Request
{
    // Query-string filters for GET Admin/Users.
    public class UserFilterRequest
    {
        public string? Role { get; set; }
        public bool? IsBlocked { get; set; }

        // Matches full name, username or email.
        public string? Search { get; set; }
    }
}
