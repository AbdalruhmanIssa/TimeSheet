using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TimeSheet.BLL.DTOs.Request;
using TimeSheet.BLL.Services.Interface;
using TimeSheet.DAL.Utilities;
using TimeSheet.PL.Extensions;

namespace TimeSheet.PL.Controllers.Employee
{
    [ApiController]
    [Area("Employee")]
    [Route("api/[area]/[controller]")]
    [Authorize(Roles = AppRoles.Employee)]
    public class TimeEntriesController : ControllerBase
    {
        private readonly ITimeEntryService _timeEntryService;

        public TimeEntriesController(ITimeEntryService timeEntryService)
        {
            _timeEntryService = timeEntryService;
        }

        // GET api/Employee/TimeEntries?status=Rejected&from=2026-10-01&to=2026-10-31&projectId=...
        [HttpGet]
        public async Task<IActionResult> GetMine([FromQuery] TimeEntryFilterRequest filter)
        {
            var entries = await _timeEntryService.GetMyEntriesAsync(User.GetUserId(), filter);
            return Ok(entries);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var entry = await _timeEntryService.GetMyEntryAsync(User.GetUserId(), id);
            return Ok(entry);
        }

        // Who approved / rejected / reopened it, with comments, oldest first.
        [HttpGet("{id:guid}/history")]
        public async Task<IActionResult> GetHistory(Guid id)
        {
            var history = await _timeEntryService.GetHistoryAsync(User.GetUserId(), id);
            return Ok(history);
        }

        [HttpPost]
        public async Task<IActionResult> Submit(SubmitTimeEntryRequest request)
        {
            var entry = await _timeEntryService.SubmitAsync(User.GetUserId(), request);
            return CreatedAtAction(nameof(GetById), new { id = entry.Id }, entry);
        }

        // Edit while PendingManager or Rejected; a Rejected entry is resubmitted.
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, UpdateTimeEntryRequest request)
        {
            var entry = await _timeEntryService.UpdateAsync(User.GetUserId(), id, request);
            return Ok(entry);
        }

        // Cancel (withdraw) while still PendingManager.
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Cancel(Guid id)
        {
            var entry = await _timeEntryService.CancelAsync(User.GetUserId(), id);
            return Ok(entry);
        }
    }
}
