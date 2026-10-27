using System.ComponentModel.DataAnnotations;

namespace TimeSheet.BLL.DTOs.Request
{
    // ?page=2&pageSize=20 on every list endpoint. Filters inherit from this.
    public class PagedRequest
    {
        public const int DefaultPageSize = 20;
        public const int MaxPageSize = 100;

        // 1-based. A page past the end returns an empty Items list (not an error).
        [Range(1, 100_000)]
        public int Page { get; set; } = 1;

        [Range(1, MaxPageSize)]
        public int PageSize { get; set; } = DefaultPageSize;
    }
}
