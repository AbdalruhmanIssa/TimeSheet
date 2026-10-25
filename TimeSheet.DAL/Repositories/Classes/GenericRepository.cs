using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using TimeSheet.DAL.Data;
using TimeSheet.DAL.Repositories.Interface;

namespace TimeSheet.DAL.Repositories.Classes
{
    public class GenericRepository<T> : IGenericRepository<T> where T : class
    {
        protected readonly AppDbContext _db;
        protected readonly DbSet<T> _set;

        public GenericRepository(AppDbContext db)
        {
            _db = db;
            _set = _db.Set<T>();
        }

        public IQueryable<T> Query() => _set.AsQueryable();

        public async Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => await _set.FindAsync(new object[] { id }, ct);

        public async Task AddAsync(T entity, CancellationToken ct = default)
            => await _set.AddAsync(entity, ct);

        public void Update(T entity) => _set.Update(entity);

        public void Remove(T entity) => _set.Remove(entity);

        public async Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default)
            => await _set.AnyAsync(predicate, ct);
    }
}
