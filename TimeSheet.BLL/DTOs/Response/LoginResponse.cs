namespace TimeSheet.BLL.DTOs.Response
{
    public class LoginResponse
    {
        public string Token { get; set; } = null!;
        public DateTime ExpiresAtUtc { get; set; }
    }
}
