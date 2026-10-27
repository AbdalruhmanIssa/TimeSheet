using TimeSheet.BLL.DTOs.Request;
using TimeSheet.BLL.DTOs.Response;

namespace TimeSheet.BLL.Services.Interface
{
    // Employee side of time entries: log, edit, cancel, list own entries.
    public interface ITimeEntryService
    {
        // Active projects the employee is actively assigned to.
        Task<List<ProjectOptionResponse>> GetProjectOptionsAsync(string employeeId);

        Task<PagedResponse<TimeEntryResponse>> GetMyEntriesAsync(string employeeId, TimeEntryFilterRequest filter);
        Task<TimeEntryResponse> GetMyEntryAsync(string employeeId, Guid entryId);

        // Review history of one of my entries, oldest first.
        Task<List<TimeEntryReviewResponse>> GetHistoryAsync(string employeeId, Guid entryId);

        Task<TimeEntryResponse> SubmitAsync(string employeeId, SubmitTimeEntryRequest request);
        Task<TimeEntryResponse> UpdateAsync(string employeeId, Guid entryId, UpdateTimeEntryRequest request);
        Task<TimeEntryResponse> CancelAsync(string employeeId, Guid entryId);
    }
}
