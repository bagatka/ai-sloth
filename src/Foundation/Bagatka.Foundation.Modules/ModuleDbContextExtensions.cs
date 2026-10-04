using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Bagatka.Foundation.Modules;

/// <summary>
/// How features commit (PATTERNS.md, entry 13).
/// </summary>
public static class ModuleDbContextExtensions
{
    /// <summary>
    /// Someone else changed the data after it was read; read it again and retry.
    /// </summary>
    public static Error ConcurrencyConflict { get; } =
        Error.Conflict("concurrency_conflict", "The data changed after it was read; read it again and retry.");

    /// <summary>
    /// Something with the same unique key was saved first, such as by a concurrent request.
    /// </summary>
    public static Error AlreadyExists { get; } =
        Error.Conflict("already_exists", "Something with the same key already exists.");

    /// <summary>
    /// Commits the context's changes in one transaction. A row changed by someone else since it was
    /// loaded becomes <see cref="ConcurrencyConflict"/>, and a duplicate unique key becomes
    /// <see cref="AlreadyExists"/>; any other failure throws.
    /// </summary>
    public static async Task<Result> SaveAsync(this DbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return new Result(new Success());
        }
        catch (DbUpdateConcurrencyException)
        {
            return new Result(ConcurrencyConflict);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return new Result(AlreadyExists);
        }
    }
}
