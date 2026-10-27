namespace TimeSheet.BLL.DTOs.Response
{
    // One page of a list, plus what the frontend needs to draw a pager.
    public class PagedResponse<T>
    {
        public List<T> Items { get; set; } = new();

        public int Page { get; set; }
        public int PageSize { get; set; }

        // All matching rows (after filters), not just this page.
        public int TotalCount { get; set; }

        public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
        public bool HasPreviousPage => Page > 1;
        public bool HasNextPage => Page < TotalPages;
    }
}
