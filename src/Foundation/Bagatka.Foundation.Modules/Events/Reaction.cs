using System;
using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.Foundation.Modules.Events;

// A registered reaction: the event type it handles, its name for logs, and how to deliver to it.
internal sealed record Reaction(string EventType, string Name, Func<IServiceProvider, string, CancellationToken, Task<Result>> DeliverAsync);
