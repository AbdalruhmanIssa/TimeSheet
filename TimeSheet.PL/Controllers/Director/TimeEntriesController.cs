using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TimeSheet.BLL.DTOs.Request;
using TimeSheet.BLL.Services.Interface;
using TimeSheet.DAL.Utilities;
using TimeSheet.PL.Extensions;

namespace TimeSheet.PL.Controllers.Director
{
    // Final approval level: PendingDirector entries on projects I direct.
    [ApiController]
    [Area("Director")]
    [Route("api/[area]/[controller]")]
    [Authorize(Roles = AppRoles.Director)]
    public class TimeEntriesController : ControllerBase
    {
        private readonly IApprovalService _approvalService;

        public TimeEntriesController(IApprovalService approvalService)
        {
            _approvalService = approvalService;
        }

        // Includes each entry's budget impact.
        [HttpGet("pending")]
        public async Task<IActionResult> GetPending([FromQuery] PagedRequest paging)
        {
            var entries = await _approvalService.GetPendingAsync(User.GetUserId(), AppRoles.Director, paging);
            return Ok(entries);
        }

        // PendingDirector -> Approved
        [HttpPost("{id:guid}/approve")]
        public async Task<IActionResult> Approve(Guid id)
        {
            var entry = await _approvalService.ApproveAsync(User.GetUserId(), AppRoles.Director, id);
            return Ok(entry);
        }

        // PendingDirector -> Rejected (comment required)
        [HttpPost("{id:guid}/reject")]
        public async Task<IActionResult> Reject(Guid id, ReviewRequest request)
        {
            var entry = await _approvalService.RejectAsync(User.GetUserId(), AppRoles.Director, id, request);
            return Ok(entry);
        }

        // Approved -> Rejected (comment required); the hours drop out of Actual right away.
        [HttpPost("{id:guid}/reopen")]
        public async Task<IActionResult> Reopen(Guid id, ReviewRequest request)
        {
            var entry = await _approvalService.ReopenAsync(User.GetUserId(), id, request);
            return Ok(entry);
        }

        [HttpPost("bulk-approve")]
        public async Task<IActionResult> BulkApprove(BulkReviewRequest request)
        {
            var result = await _approvalService.BulkApproveAsync(User.GetUserId(), AppRoles.Director, request);
            return Ok(result);
        }
    }
}
