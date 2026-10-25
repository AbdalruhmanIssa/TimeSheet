namespace TimeSheet.BLL.DTOs.Response
{
    public class ProjectEmployeeResponse
    {
        public string EmployeeId { get; set; } = null!;
        public string FullName { get; set; } = null!;
        public string UserName { get; set; } = null!;
        public bool IsActive { get; set; }
        public DateTime AssignedAt { get; set; }
    }
}
