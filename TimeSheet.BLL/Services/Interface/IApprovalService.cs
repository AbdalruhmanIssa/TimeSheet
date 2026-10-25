using TimeSheet.BLL.DTOs.Request;
using TimeSheet.BLL.DTOs.Response;

namespace TimeSheet.BLL.Services.Interface
{
    // Reviewer side of time entries. `role` is Manager or Director:
    // Manager acts on PendingManager entries of projects he manages,
    // Director on PendingDirector entries of projects he directs.
    public interface IApprovalService
    {
        Task<List<PendingReviewResponse>> GetPendingAsync(string reviewerId, string role);

        Task<PendingReviewResponse> ApproveAsync(string reviewerId, string role, Guid entryId);
        Task<PendingReviewResponse> RejectAsync(string reviewerId, string role, Guid entryId, ReviewRequest request);
        Task<BulkReviewResponse> BulkApproveAsync(string reviewerId, string role, BulkReviewRequest request);

        // Director only: Approved -> Rejected, comment required.
        Task<PendingReviewResponse> ReopenAsync(string directorId, Guid entryId, ReviewRequest request);
    }
}
