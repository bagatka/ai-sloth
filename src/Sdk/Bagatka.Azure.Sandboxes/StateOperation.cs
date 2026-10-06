using System;
using System.Threading;
using System.Threading.Tasks;
using Azure;

namespace Bagatka.Azure.Sandboxes;

// A change the service finishes on its own time without an operation resource of its own, such as a
// sandbox starting: polling the resource until it is in a state where the change is done, or one
// where it failed. This library is async only, so the synchronous members aren't supported.
internal sealed class StateOperation<T> : Operation<T>
    where T : notnull
{
    private readonly Func<CancellationToken, Task<Response<T>>> _poll;
    private readonly Func<T, bool> _done;
    private readonly Func<T, string?> _failure;
    private Response _rawResponse;
    private T? _value;
    private RequestFailedException? _failed;
    private bool _completed;

    public StateOperation(string id, Response<T> started, Func<CancellationToken, Task<Response<T>>> poll, Func<T, bool> done, Func<T, string?> failure)
    {
        Id = id;
        _poll = poll;
        _done = done;
        _failure = failure;
        _rawResponse = started.GetRawResponse();
        Apply(started.Value);
    }

    public override string Id { get; }

    public override T Value
    {
        get
        {
            if (_failed is not null)
            {
                throw _failed;
            }

            if (!HasCompleted || _value is null)
            {
                throw new InvalidOperationException("The operation hasn't completed; wait for it first.");
            }

            return _value;
        }
    }

    public override bool HasValue => HasCompleted && _failed is null;

    public override bool HasCompleted => _completed;

    public override Response GetRawResponse()
    {
        return _rawResponse;
    }

    public override Response UpdateStatus(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Bagatka.Azure.Sandboxes is async only: use UpdateStatusAsync or WaitForCompletionAsync.");
    }

    public override async ValueTask<Response> UpdateStatusAsync(CancellationToken cancellationToken = default)
    {
        if (HasCompleted)
        {
            return _rawResponse;
        }

        Response<T> polled = await _poll(cancellationToken).ConfigureAwait(false);
        _rawResponse = polled.GetRawResponse();
        Apply(polled.Value);
        return _rawResponse;
    }

    private void Apply(T current)
    {
        string? failure = _failure(current);
        if (failure is not null)
        {
            _failed = new RequestFailedException(_rawResponse.Status, failure);
            _completed = true;
        }
        else if (_done(current))
        {
            _value = current;
            _completed = true;
        }
    }
}
