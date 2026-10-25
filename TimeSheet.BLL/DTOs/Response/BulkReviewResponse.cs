namespace TimeSheet.BLL.DTOs.Response
{
    public class BulkReviewResponse
    {
        public List<Guid> Approved { get; set; } = new();
        public List<BulkReviewFailure> Failed { get; set; } = new();
    }

    public class BulkReviewFailure
    {
        public Guid Id { get; set; }
        public string Reason { get; set; } = null!;
    }
}
