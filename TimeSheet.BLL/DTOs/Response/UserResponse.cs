namespace TimeSheet.BLL.DTOs.Response
{
    public class UserResponse
    {
        public string Id { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string UserName { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string? PhoneNumber { get; set; }
        public string? Role { get; set; }
        public bool IsBlocked { get; set; }
    }
}
