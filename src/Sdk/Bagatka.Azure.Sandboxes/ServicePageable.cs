using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure;

namespace Bagatka.Azure.Sandboxes;

// A list the service returns a page at a time, each pointing at the next with its link.
internal sealed class ServicePageable<T>(Func<string?, CancellationToken, Task<Page<T>>> page, CancellationToken cancellationToken) : AsyncPageable<T>(cancellationToken)
    where T : notnull
{
    public override async IAsyncEnumerable<Page<T>> AsPages(string? continuationToken = null, int? pageSizeHint = null)
    {
        string? next = continuationToken;
        do
        {
            Page<T> current = await page(next, CancellationToken).ConfigureAwait(false);
            yield return current;
            next = current.ContinuationToken;
        }
        while (next is not null);
    }
}
