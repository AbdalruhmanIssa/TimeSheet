using System.ComponentModel.DataAnnotations;

namespace TimeSheet.BLL.DTOs.Request
{
    public class AssignEmployeesRequest
    {
        // Users with the Employee role. If any of them is already on the team the whole
        // request is refused; previously removed ones are re-activated.
        [Required, MinLength(1)]
        public List<string> EmployeeIds { get; set; } = new();
    }
}
