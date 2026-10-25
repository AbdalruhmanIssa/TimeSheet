namespace TimeSheet.BLL.DTOs.Response
{
    public class ProjectDetailsResponse : ProjectResponse
    {
        // True once the project has an Approved entry: BudgetHours can't change any more.
        public bool IsBudgetLocked { get; set; }

        // Active assignments first, then removed ones (history).
        public List<ProjectEmployeeResponse> Employees { get; set; } = new();
    }
}
