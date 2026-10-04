namespace Bagatka.AiSloth.Chats.Harness;

// What reading an agent's process yields: a complete line of its standard output, or its exit.
internal union AgentOutput(HarnessLine, HarnessExited);
