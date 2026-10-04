using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;

namespace Bagatka.Foundation.Modules;

/// <summary>
/// Keyset pagination over typed IDs (PATTERNS.md, entry 18). A cursor is the last item's ID,
/// opaque to callers.
/// </summary>
public static class Keyset
{
    /// <summary>A cursor this list didn't issue.</summary>
    public static Error InvalidCursor { get; } =
        Error.Validation("cursor", "The cursor is invalid; pass the nextCursor of the previous page.");

    /// <summary>
    /// Orders the query by <paramref name="key"/>, starts after the page's cursor, and fetches one
    /// item more than the page holds, so <see cref="ToPage"/> can tell whether another page follows.
    /// </summary>
    /// <returns>The query; or <see cref="InvalidCursor"/>.</returns>
    public static Result<IQueryable<T>> TakePage<T, TId>(this IQueryable<T> query, Expression<Func<T, TId>> key, KeysetOrder order, PageRequest page)
        where TId : struct, ITypedId<TId>
    {
        ArgumentNullException.ThrowIfNull(page);
        IQueryable<T> ordered = order switch
        {
            KeysetOrder.OldestFirst => query.OrderBy(key),
            KeysetOrder.NewestFirst => query.OrderByDescending(key),
        };

        if (page.Cursor is not null)
        {
            bool parsed = Guid.TryParseExact(page.Cursor, "N", out Guid last);
            if (!parsed)
            {
                return new Result<IQueryable<T>>(InvalidCursor);
            }

            ExpressionType comparison = order == KeysetOrder.OldestFirst ? ExpressionType.GreaterThan : ExpressionType.LessThan;
            ordered = ordered.Where(Beyond(key, TId.From(last), comparison));
        }

        return new Result<IQueryable<T>>(ordered.Take(page.Limit + 1));
    }

    /// <summary>The page from the items <see cref="TakePage"/> fetched, in the same order.</summary>
    public static Page<T> ToPage<T>(IReadOnlyList<T> fetched, PageRequest page, Func<T, Guid> key)
    {
        ArgumentNullException.ThrowIfNull(fetched);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(key);
        if (fetched.Count <= page.Limit)
        {
            return new Page<T>(fetched, NextCursor: null);
        }

        List<T> items = fetched.Take(page.Limit).ToList();
        return new Page<T>(items, key(items[^1]).ToString("N", CultureInfo.InvariantCulture));
    }

    // `item.Id > bound` for a typed ID, which has no comparison operators. EF Core translates the
    // comparison on the stored GUIDs and never calls the method the expression names.
    private static Expression<Func<T, bool>> Beyond<T, TId>(Expression<Func<T, TId>> key, TId bound, ExpressionType comparison)
        where TId : struct
    {
        Func<TId, TId, bool> translatedOnly = TranslatedOnly;
        Expression<Func<TId>> boundValue = () => bound;
        BinaryExpression body = Expression.MakeBinary(comparison, key.Body, boundValue.Body, liftToNull: false, translatedOnly.Method);
        return Expression.Lambda<Func<T, bool>>(body, key.Parameters);
    }

    private static bool TranslatedOnly<TId>(TId left, TId right)
    {
        throw new InvalidOperationException("Keyset comparisons are only translated to SQL.");
    }
}
