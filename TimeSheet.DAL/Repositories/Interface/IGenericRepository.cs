using System.Linq.Expressions;

namespace TimeSheet.DAL.Repositories.Interface
{
    public interface IGenericRepository<T> where T : class
    {
        Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default);
        IQueryable<T> Query();

        Task AddAsync(T entity, CancellationToken ct = default);
        void Update(T entity);
        void Remove(T entity);

        Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default);
    }
}
