namespace TimeSheet.BLL.DTOs.Response
{
    // Per-employee breakdown of a project's hours.
    public class EmployeeHoursRow
    {
        public string EmployeeId { get; set; } = null!;
        public string EmployeeName { get; set; } = null!;

        // Currently on the team (false = removed, but has hours in the range).
        public bool IsActiveOnProject { get; set; }

        // Approved hours in the range.
        public decimal ApprovedHours { get; set; }

        // Share of the project's approved hours in the range (2 decimals; 0 when nothing is approved).
        public decimal SharePercent { get; set; }

        // PendingManager + PendingDirector hours in the range.
        public decimal PendingHours { get; set; }
    }
}
