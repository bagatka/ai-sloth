namespace Bagatka.AiSloth.Chats.Harness;

// EndOffset is the output offset just after the line, where reading resumes once it is handled.
internal sealed record HarnessLine(string Text, long EndOffset);
