namespace TimeSheet.BLL.DTOs.Response
{
    public class ClientResponse
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = null!;
        public string? ContactName { get; set; }
        public string? ContactEmail { get; set; }
        public string? Phone { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
