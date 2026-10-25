using System.ComponentModel.DataAnnotations;

namespace TimeSheet.BLL.DTOs.Request
{
    public class AssignEmployeesRequest
    {
        // Users with the Employee role. Already-assigned ones are ignored;
        // previously removed ones are re-activated.
        [Required, MinLength(1)]
        public List<string> EmployeeIds { get; set; } = new();
    }
}
