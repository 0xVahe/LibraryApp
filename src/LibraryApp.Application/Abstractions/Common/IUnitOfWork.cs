namespace LibraryApp.Application.Abstractions.Common;

/// <summary>
/// One business operation = one database transaction (BR-BOR-4), even when it calls several modules.
/// <list type="bullet">
/// <item>The module that starts the use case opens the transaction here.</item>
/// <item>Providers called INSIDE it (for example SetCopyStatusAsync) only change data: no SaveChanges, no own transaction.</item>
/// <item>The delegate can run again after a short network failure: do not send e-mails or call HTTP inside it.</item>
/// </list>
/// </summary>
public interface IUnitOfWork
{
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default);

    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default);
}
