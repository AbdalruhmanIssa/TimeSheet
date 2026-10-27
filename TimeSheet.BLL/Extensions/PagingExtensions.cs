using Microsoft.EntityFrameworkCore;
using TimeSheet.BLL.DTOs.Request;
using TimeSheet.BLL.DTOs.Response;

namespace TimeSheet.BLL.Extensions
{
    public static class PagingExtensions
    {
        // COUNT(*) + one page (OFFSET/FETCH) in SQL. The query must already be ordered
        // by a unique key so pages don't overlap or skip rows.
        public static async Task<PagedResponse<T>> ToPagedAsync<T>(this IQueryable<T> query, PagedRequest paging)
        {
            var total = await query.CountAsync();
            var items = await query
                .Skip((paging.Page - 1) * paging.PageSize)
                .Take(paging.PageSize)
                .ToListAsync();

            return new PagedResponse<T>
            {
                Items = items,
                Page = paging.Page,
                PageSize = paging.PageSize,
                TotalCount = total
            };
        }

        // Same, for lists that are built and sorted in memory (e.g. the portfolio).
        public static PagedResponse<T> ToPaged<T>(this IReadOnlyCollection<T> source, PagedRequest paging)
        {
            return new PagedResponse<T>
            {
                Items = source
                    .Skip((paging.Page - 1) * paging.PageSize)
                    .Take(paging.PageSize)
                    .ToList(),
                Page = paging.Page,
                PageSize = paging.PageSize,
                TotalCount = source.Count
            };
        }
    }
}
