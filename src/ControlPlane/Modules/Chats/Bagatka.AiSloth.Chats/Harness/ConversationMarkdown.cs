using System;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Model;

namespace Bagatka.AiSloth.Chats.Harness;

// A chat's conversation as plain Markdown, for the agent of a chat that continues it: what people
// wrote, what the agent answered, and a line for each thing it did. Its thinking and its tools' output
// are left out, so it reads in far fewer tokens than the session it comes from. Built event by event,
// in order.
internal sealed class ConversationMarkdown
{
    // A conversation longer than this keeps its end, where the work it continues stands.
    private const int MaxLength = 2_000_000;

    private readonly StringBuilder _text = new StringBuilder();
    private readonly Chat _chat;
    private string? _title;
    private Block _last = Block.None;

    public ConversationMarkdown(Chat chat)
    {
        _chat = chat;
    }

    private enum Block
    {
        None,
        Person,
        AgentText,
        AgentActions,
    }

    // Where the conversation of a chat is in the nook of a chat that continues it, and in copies of that
    // nook after.
    public static string PathOf(ChatId chat)
    {
        return string.Create(CultureInfo.InvariantCulture, $"/work/.aisloth/chats/{chat.Value}.md");
    }

    // Takes the chat's events in order; only messages and the agent's updates show.
    public void Add(StoredEvent stored)
    {
        switch (stored.ToContract().Body)
        {
            case MessageSent sent:
                _title ??= Chat.TitleFrom(sent.Text);
                _text.Append(_last is Block.None ? "## Person\n\n" : "\n## Person\n\n").Append(sent.Text.Trim()).Append('\n');
                _last = Block.Person;
                break;
            case AgentUpdate update:
                AddUpdate(update.Update);
                break;
        }
    }

    public string ToMarkdown()
    {
        string intro = "# " + (_title ?? "An earlier chat") + "\n\n"
            + "The conversation of an earlier chat in AiSloth, which this chat continues in a copy of its files: what people wrote, what "
            + "the agent (" + _chat.Harness + (_chat.Model is null ? string.Empty : ", on " + _chat.Model) + ") answered, and a line for "
            + "each thing it did. Its thinking and its tools' output are left out.\n";
        if (_chat.ContinuesFrom is ChatId earlier)
        {
            intro += "\nThat chat continued another, whose conversation is in `" + PathOf(earlier) + "`.\n";
        }

        string body = _text.ToString();
        if (body.Length > MaxLength)
        {
            intro += "\nIts start is left out: the whole is too long to read.\n";
            body = body[^MaxLength..];
        }

        return intro + "\n" + body;
    }

    // ACP's updates (https://agentclientprotocol.com/protocol/prompt-turn): the agent's text comes in
    // chunks, each thing it does as a tool call with a title.
    private void AddUpdate(JsonElement update)
    {
        string? kind = Text(update, "sessionUpdate");
        JsonElement content = Property(update, "content");
        if (string.Equals(kind, "agent_message_chunk", StringComparison.Ordinal) && string.Equals(Text(content, "type"), "text", StringComparison.Ordinal))
        {
            _text.Append(_last switch
            {
                Block.AgentText => string.Empty,
                Block.AgentActions => "\n",
                Block.Person => "\n## Agent\n\n",
                _ => "## Agent\n\n",
            });
            _text.Append(Text(content, "text"));
            _last = Block.AgentText;
        }
        else if (string.Equals(kind, "tool_call", StringComparison.Ordinal))
        {
            _text.Append(_last switch
            {
                Block.AgentActions => string.Empty,
                Block.AgentText => "\n\n",
                Block.Person => "\n## Agent\n\n",
                _ => "## Agent\n\n",
            });
            _text.Append("- ").Append(Text(update, "title") ?? "A tool call").Append('\n');
            _last = Block.AgentActions;
        }
    }

    private static JsonElement Property(JsonElement holder, string name)
    {
        if (holder.ValueKind != JsonValueKind.Object)
        {
            return default;
        }

        bool present = holder.TryGetProperty(name, out JsonElement value);
        return present ? value : default;
    }

    private static string? Text(JsonElement holder, string name)
    {
        JsonElement value = Property(holder, name);
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
