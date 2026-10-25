namespace TimeSheet.DAL.Utilities
{
    // Role names used for Identity roles, the "Role" JWT claim and [Authorize(Roles = ...)].
    public static class AppRoles
    {
        public const string Admin = "Admin";
        public const string Director = "Director";
        public const string Manager = "Manager";
        public const string Employee = "Employee";

        public static readonly IReadOnlyList<string> All = [Admin, Director, Manager, Employee];
    }
}
