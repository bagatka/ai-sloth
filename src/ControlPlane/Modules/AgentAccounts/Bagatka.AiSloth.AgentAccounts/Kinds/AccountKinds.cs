using System.Collections.Frozen;
using System.Collections.Generic;
using Bagatka.AiSloth.AgentAccounts.Contracts;

namespace Bagatka.AiSloth.AgentAccounts.Kinds;

// Every kind of account this module knows, in the order clients offer them.
internal sealed class AccountKinds(IEnumerable<IAccountKind> kinds)
{
    private readonly FrozenDictionary<AgentAccountKind, IAccountKind> _byId = kinds.ToFrozenDictionary(kind => kind.Id);

    public IReadOnlyList<IAccountKind> All { get; } = [.. kinds];

    // The kind, or null for one this module doesn't know, such as a client's mistake.
    public IAccountKind? Find(AgentAccountKind id)
    {
        return _byId.GetValueOrDefault(id);
    }

    // The kind of a stored account or sign-in, which is always known.
    public IAccountKind Of(AgentAccountKind id)
    {
        return _byId[id];
    }
}
