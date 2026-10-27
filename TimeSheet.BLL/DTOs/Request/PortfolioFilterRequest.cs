using TimeSheet.DAL.Enums;

namespace TimeSheet.BLL.DTOs.Request
{
    // Query-string filters for GET Director/Reports/portfolio.
    public class PortfolioFilterRequest : PagedRequest
    {
        public ProjectStatus? Status { get; set; }
    }
}
