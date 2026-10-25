namespace TimeSheet.BLL.DTOs.Request
{
    // Query-string filters for GET Director/Clients.
    public class ClientFilterRequest
    {
        // Matches client name or contact name.
        public string? Search { get; set; }

        // Deactivated clients are hidden unless this is true.
        public bool IncludeInactive { get; set; }
    }
}
