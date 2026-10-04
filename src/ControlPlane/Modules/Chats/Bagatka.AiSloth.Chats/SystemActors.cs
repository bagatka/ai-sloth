using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats;

// The module's own actor: the runners that start agents, feed them, and fetch their accounts' secrets.
internal static class SystemActors
{
    public static readonly Actor Harness = Actor.ForSystem("chats.harness");
}
