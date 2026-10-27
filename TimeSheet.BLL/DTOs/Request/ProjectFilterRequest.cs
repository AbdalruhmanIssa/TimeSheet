using TimeSheet.DAL.Enums;

namespace TimeSheet.BLL.DTOs.Request
{
    // Query-string filters for GET Director/Projects and Manager/Projects.
    public class ProjectFilterRequest : PagedRequest
    {
        public ProjectStatus? Status { get; set; }
        public Guid? ClientId { get; set; }

        // Matches project code or name.
        public string? Search { get; set; }
    }
}
