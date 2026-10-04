using Bagatka.AiSloth.Nooks.Contracts;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// Input to <see cref="IChatsApi.StartAsync"/>.
/// </summary>
/// <param name="NookId">The nook the agent will work in.</param>
public sealed record StartChat(NookId NookId);
