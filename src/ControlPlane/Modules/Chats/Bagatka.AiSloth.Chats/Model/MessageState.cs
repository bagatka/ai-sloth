namespace Bagatka.AiSloth.Chats.Model;

// Where a message is on its way to the agent. New messages aren't announced yet; Steering ones are
// offered to the running turn and await the agent's answer.
internal enum MessageState
{
    New = 1,
    Queued = 2,
    Steering = 3,
    Delivered = 4,
    Cancelled = 5,

    // An announced proposal: it never reaches the agent.
    Proposed = 6,
}
