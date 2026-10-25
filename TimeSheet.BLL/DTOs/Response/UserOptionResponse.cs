namespace TimeSheet.BLL.DTOs.Response
{
    // Minimal user row for pickers (e.g. choosing a project's manager / employees).
    public class UserOptionResponse
    {
        public string Id { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string UserName { get; set; } = null!;
    }
}
