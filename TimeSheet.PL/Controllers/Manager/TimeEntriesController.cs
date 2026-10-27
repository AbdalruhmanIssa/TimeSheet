using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TimeSheet.BLL.DTOs.Request;
using TimeSheet.BLL.Services.Interface;
using TimeSheet.DAL.Utilities;
using TimeSheet.PL.Extensions;

namespace TimeSheet.PL.Controllers.Manager
{
    // First approval level: PendingManager entries on projects I manage.
    [ApiController]
    [Area("Manager")]
    [Route("api/[area]/[controller]")]
    [Authorize(Roles = AppRoles.Manager)]
    public class TimeEntriesController : ControllerBase
    {
        private readonly IApprovalService _approvalService;

        public TimeEntriesController(IApprovalService approvalService)
        {
            _approvalService = approvalService;
        }

        [HttpGet("pending")]
        public async Task<IActionResult> GetPending([FromQuery] PagedRequest paging)
        {
            var entries = await _approvalService.GetPendingAsync(User.GetUserId(), AppRoles.Manager, paging);
            return Ok(entries);
        }

        // PendingManager -> PendingDirector
        [HttpPost("{id:guid}/approve")]
        public async Task<IActionResult> Approve(Guid id)
        {
            var entry = await _approvalService.ApproveAsync(User.GetUserId(), AppRoles.Manager, id);
            return Ok(entry);
        }

        // PendingManager -> Rejected (comment required)
        [HttpPost("{id:guid}/reject")]
        public async Task<IActionResult> Reject(Guid id, ReviewRequest request)
        {
            var entry = await _approvalService.RejectAsync(User.GetUserId(), AppRoles.Manager, id, request);
            return Ok(entry);
        }

        [HttpPost("bulk-approve")]
        public async Task<IActionResult> BulkApprove(BulkReviewRequest request)
        {
            var result = await _approvalService.BulkApproveAsync(User.GetUserId(), AppRoles.Manager, request);
            return Ok(result);
        }
    }
}
