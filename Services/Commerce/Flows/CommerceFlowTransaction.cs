using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using WebApplication2.Models;

namespace WebApplication2.Services.Commerce.Flows;

public static class CommerceFlowTransaction
{
    public static async Task<T> ExecuteAsync<T>(
        ApplicationDbContext context,
        CommerceFlowTracker tracker,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken,
        IsolationLevel isolationLevel = IsolationLevel.Serializable)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tracker);
        ArgumentNullException.ThrowIfNull(operation);

        IDbContextTransaction? transaction = null;
        try
        {
            tracker.MoveTo(CommerceFlowStage.BeginTransaction);
            transaction = await context.Database.BeginTransactionAsync(
                isolationLevel,
                cancellationToken);

            var result = await operation(cancellationToken);

            tracker.MoveTo(CommerceFlowStage.Commit);
            await transaction.CommitAsync(cancellationToken);
            tracker.MoveTo(CommerceFlowStage.Complete);
            return result;
        }
        catch (CommerceFlowException)
        {
            await TryRollbackAsync(transaction, tracker, cancellationToken);
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await TryRollbackAsync(transaction, tracker, CancellationToken.None);
            throw;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            await TryRollbackAsync(transaction, tracker, cancellationToken);
            throw new CommerceConcurrencyException(
                "COMMERCE_CONCURRENCY_CONFLICT",
                "Dữ liệu đã được cập nhật bởi một tiến trình khác. Hãy tải lại aggregate trước khi thử lại.",
                tracker.Snapshot(),
                exception);
        }
        catch (DbUpdateException exception)
        {
            await TryRollbackAsync(transaction, tracker, cancellationToken);
            throw new CommerceTransactionException(
                "COMMERCE_DATABASE_UPDATE_FAILED",
                "Database từ chối thay đổi trong commerce flow.",
                tracker.Snapshot(),
                exception);
        }
        catch (Exception exception)
        {
            await TryRollbackAsync(transaction, tracker, cancellationToken);
            throw new CommerceTransactionException(
                "COMMERCE_TRANSACTION_FAILED",
                "Commerce flow thất bại trong transaction.",
                tracker.Snapshot(),
                exception);
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private static async Task TryRollbackAsync(
        IDbContextTransaction? transaction,
        CommerceFlowTracker tracker,
        CancellationToken cancellationToken)
    {
        if (transaction is null)
        {
            return;
        }

        var failedStage = tracker.Stage;
        try
        {
            tracker.MoveTo(CommerceFlowStage.Rollback, failedStage.ToString());
            await transaction.RollbackAsync(cancellationToken);
        }
        catch (Exception rollbackException)
        {
            tracker.AddMetadata("RollbackException", rollbackException.Message);
        }
        finally
        {
            tracker.MoveTo(failedStage);
        }
    }
}
