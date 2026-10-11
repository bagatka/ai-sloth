using System;
using System.Buffers;
using System.Text.Json;
using Bagatka.AiSloth.Chats.Contracts;

namespace Bagatka.AiSloth.WebApi;

// The usage a model's answer reports, read from its bytes as the gateway passes them to the agent:
// Anthropic's Messages API (message_start, then message_delta's running counts), OpenAI's Responses
// API (response.completed), and its chat API (the chunk or answer that carries usage), streamed as
// server-sent events or whole. Only a line or body that mentions usage is parsed, and none longer than
// a megabyte is kept, so a long answer costs nothing more. One answer per reader; not thread-safe.
internal sealed class ModelUsageReader(bool streamed)
{
    private const int MaxKept = 1 << 20;

    private readonly ArrayBufferWriter<byte> _kept = new ArrayBufferWriter<byte>();
    private bool _tooLong;
    private string? _model;
    private bool _found;
    private int _input;
    private int _cacheRead;
    private int _cacheWrite;
    private int _cacheWriteLong;
    private int _output;
    private int _reasoning;
    private decimal? _cost;

    public void Read(ReadOnlySpan<byte> bytes)
    {
        if (!streamed)
        {
            Keep(bytes);
            return;
        }

        while (bytes.Length > 0)
        {
            int end = bytes.IndexOf((byte)'\n');
            Keep(end < 0 ? bytes : bytes[..end]);
            if (end < 0)
            {
                return;
            }

            if (!_tooLong)
            {
                Line(_kept.WrittenSpan);
            }

            _kept.Clear();
            _tooLong = false;
            bytes = bytes[(end + 1)..];
        }
    }

    // What the answer reported, once all of it passed; null when it reported nothing this reads.
    public ModelCallUsage? Usage()
    {
        if (!_tooLong && _kept.WrittenCount > 0)
        {
            if (streamed)
            {
                Line(_kept.WrittenSpan);
            }
            else
            {
                Json(_kept.WrittenSpan);
            }
        }

        return _found && _model is string model
            ? new ModelCallUsage(model, _input, _cacheRead, _cacheWrite, _cacheWriteLong, _output, _reasoning, _cost)
            : null;
    }

    private void Keep(ReadOnlySpan<byte> bytes)
    {
        if (_tooLong || _kept.WrittenCount + bytes.Length > MaxKept)
        {
            _tooLong = true;
            _kept.Clear();
            return;
        }

        _kept.Write(bytes);
    }

    private void Line(ReadOnlySpan<byte> line)
    {
        ReadOnlySpan<byte> data = line.TrimEnd((byte)'\r');
        bool isData = data.StartsWith("data:"u8);
        if (isData && data.IndexOf("usage"u8) >= 0)
        {
            Json(data["data:".Length..].TrimStart((byte)' '));
        }
    }

    private void Json(ReadOnlySpan<byte> json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json.ToArray());
        }
        catch (JsonException)
        {
            return;
        }

        using (document)
        {
            Answer(document.RootElement);
        }
    }

    // The object that names the model and holds the usage: Anthropic's message at its start, OpenAI's
    // response when completed, or the event or answer itself.
    private void Answer(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        // The answer's type, for the events that hold the usage elsewhere.
        string? type = Text(root, "type");
        JsonElement holder = type switch
        {
            "message_start" => Nested(root, "message"),
            "response.completed" or "response.incomplete" => Nested(root, "response"),
            _ => root,
        };
        _model = Text(holder, "model") ?? _model;
        JsonElement usage = Nested(holder, "usage");
        if (usage.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        _found = true;
        bool openAIResponses = Nested(usage, "input_tokens_details").ValueKind != JsonValueKind.Undefined || Nested(usage, "output_tokens_details").ValueKind != JsonValueKind.Undefined;
        bool chat = Nested(usage, "prompt_tokens").ValueKind != JsonValueKind.Undefined;
        if (chat)
        {
            ChatUsage(usage);
        }
        else if (openAIResponses)
        {
            ResponsesUsage(usage);
        }
        else
        {
            AnthropicUsage(usage);
        }
    }

    // OpenAI's chat API: prompt tokens count the cached ones; OpenRouter adds what the call cost.
    private void ChatUsage(JsonElement usage)
    {
        int prompt = Count(usage, "prompt_tokens");
        int cached = Count(Nested(usage, "prompt_tokens_details"), "cached_tokens");
        _input = Math.Max(0, prompt - cached);
        _cacheRead = cached;
        _output = Count(usage, "completion_tokens");
        _reasoning = Count(Nested(usage, "completion_tokens_details"), "reasoning_tokens");
        JsonElement cost = Nested(usage, "cost");
        if (cost.ValueKind != JsonValueKind.Number)
        {
            return;
        }

        bool read = cost.TryGetDecimal(out decimal dollars);
        _cost = read && dollars >= 0 ? dollars : _cost;
    }

    // OpenAI's Responses API: input tokens count the cached ones.
    private void ResponsesUsage(JsonElement usage)
    {
        int input = Count(usage, "input_tokens");
        int cached = Count(Nested(usage, "input_tokens_details"), "cached_tokens");
        _input = Math.Max(0, input - cached);
        _cacheRead = cached;
        _output = Count(usage, "output_tokens");
        _reasoning = Count(Nested(usage, "output_tokens_details"), "reasoning_tokens");
    }

    // Anthropic's: input tokens leave out those read from or written to a cache, which it counts apart,
    // writes by how long they're kept; message_delta's counts run on, so each count keeps its largest.
    private void AnthropicUsage(JsonElement usage)
    {
        _input = Math.Max(_input, Count(usage, "input_tokens"));
        _cacheRead = Math.Max(_cacheRead, Count(usage, "cache_read_input_tokens"));
        JsonElement writes = Nested(usage, "cache_creation");
        int written = Count(usage, "cache_creation_input_tokens");
        int writtenLong = Count(writes, "ephemeral_1h_input_tokens");
        _cacheWriteLong = Math.Max(_cacheWriteLong, writtenLong);
        _cacheWrite = Math.Max(_cacheWrite, Math.Max(0, written - writtenLong));
        _output = Math.Max(_output, Count(usage, "output_tokens"));
    }

    private static JsonElement Nested(JsonElement holder, string name)
    {
        if (holder.ValueKind != JsonValueKind.Object)
        {
            return default;
        }

        bool has = holder.TryGetProperty(name, out JsonElement nested);
        return has ? nested : default;
    }

    private static int Count(JsonElement holder, string name)
    {
        JsonElement count = Nested(holder, name);
        if (count.ValueKind != JsonValueKind.Number)
        {
            return 0;
        }

        bool fits = count.TryGetInt32(out int value);
        return fits && value >= 0 ? value : 0;
    }

    private static string? Text(JsonElement holder, string name)
    {
        JsonElement text = Nested(holder, name);
        return text.ValueKind == JsonValueKind.String ? text.GetString() : null;
    }
}
