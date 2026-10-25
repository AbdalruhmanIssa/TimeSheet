namespace TimeSheet.BLL.DTOs.Response
{
    // Employee's project dropdown when logging hours.
    public class ProjectOptionResponse
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = null!;
        public string Name { get; set; } = null!;
    }
}
