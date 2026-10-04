namespace Bagatka.AiSloth.Chats.Harness;

// What a chat's runner reacts to: a nudge to look at the chat again, a line the agent wrote, the
// agent's process ending, or the output reader failing.
internal union RunnerInput(WakeUp, HarnessLine, HarnessExited, ReaderFailed);
