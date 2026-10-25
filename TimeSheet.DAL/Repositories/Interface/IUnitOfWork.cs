using System.Data;

namespace TimeSheet.DAL.Repositories.Interface
{
    public interface IUnitOfWork : IDisposable
    {
        IGenericRepository<TEntity> Repo<TEntity>() where TEntity : class;
        Task<int> SaveChangesAsync(CancellationToken ct = default);

        // Drops all tracked (unsaved) changes — used after a failed item in a batch
        // so the next item's SaveChanges doesn't retry the failed one.
        void ClearChanges();

        // Runs the operation inside a DB transaction, wrapped in EF's execution
        // strategy so it works with EnableRetryOnFailure. If a transient SQL error
        // happens, the WHOLE operation (reads + writes) is retried as one unit.
        Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<Task<TResult>> operation,
            CancellationToken ct = default);

        // Same as above, but with an explicit isolation level (e.g. Serializable
        // for check-then-insert rules like the daily hours cap).
        Task<TResult> ExecuteInTransactionAsync<TResult>(
            Func<Task<TResult>> operation,
            IsolationLevel isolationLevel,
            CancellationToken ct = default);
    }
}
