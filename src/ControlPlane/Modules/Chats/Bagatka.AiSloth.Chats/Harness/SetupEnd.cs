namespace Bagatka.AiSloth.Chats.Harness;

// How a setup run ended: its exit code, and the end of its output.
internal sealed record SetupEnd(int ExitCode, string Output);
