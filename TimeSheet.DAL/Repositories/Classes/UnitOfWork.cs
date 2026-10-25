using Microsoft.EntityFrameworkCore;
using System.Data;
using TimeSheet.DAL.Data;
using TimeSheet.DAL.Repositories.Interface;

namespace TimeSheet.DAL.Repositories.Classes
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly AppDbContext _db;
        private readonly Dictionary<Type, object> _repos = new();

        public UnitOfWork(AppDbContext db)
        {
            _db = db;
        }

        public IGenericRepository<TEntity> Repo<TEntity>() where TEntity : class
        {
            var type = typeof(TEntity);

            if (_repos.TryGetValue(type, out var repo))
                return (IGenericRepository<TEntity>)repo;

            var newRepo = new GenericRepository<TEntity>(_db);
            _repos[type] = newRepo;
            return newRepo;
        }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
            => _db.SaveChangesAsync(ct);

        public void ClearChanges() => _db.ChangeTracker.Clear();

        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<Task<TResult>> operation,
            CancellationToken ct = default)
            => ExecuteInTransactionCoreAsync(operation, isolationLevel: null, ct);

        public Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<Task<TResult>> operation,
            IsolationLevel isolationLevel,
            CancellationToken ct = default)
            => ExecuteInTransactionCoreAsync(operation, isolationLevel, ct);

        // null isolation level = SQL Server default (READ COMMITTED).
        private Task<TResult> ExecuteInTransactionCoreAsync<TResult>(
            Func<Task<TResult>> operation,
            IsolationLevel? isolationLevel,
            CancellationToken ct)
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            var attempt = 0;

            return strategy.ExecuteAsync(async () =>
            {
                // On a retry, drop entities tracked by the failed attempt so they
                // aren't inserted twice.
                if (attempt++ > 0)
                    _db.ChangeTracker.Clear();

                await using var transaction = isolationLevel is { } level
                    ? await _db.Database.BeginTransactionAsync(level, ct)
                    : await _db.Database.BeginTransactionAsync(ct);

                var result = await operation();
                await transaction.CommitAsync(ct);
                return result;
            });
        }

        public void Dispose() => _db.Dispose();
    }
}
