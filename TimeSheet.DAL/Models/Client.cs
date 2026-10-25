namespace TimeSheet.DAL.Models
{
    // The employer a project is done for.
    public class Client : BaseEntity
    {
        public string Name { get; set; } = null!;
        public string? ContactName { get; set; }
        public string? ContactEmail { get; set; }
        public string? Phone { get; set; }

        // Soft-disable instead of delete.
        public bool IsActive { get; set; } = true;
    }
}
