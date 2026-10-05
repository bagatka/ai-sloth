using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.AiSloth.Cli;

internal sealed partial class Sloth
{
    // Shows a chat's events as text as they arrive: people's messages, what the agent says and does,
    // and how each turn ends. Between events it keeps whether a line is open, the agent's tool calls,
    // people's names, and the messages typed here, which the terminal already shows. Agent updates
    // without a line here (thoughts, plans, the agent's commands and modes) aren't shown.
    private sealed class ChatPrinter(Terminal output, HostApi api, Guid me)
    {
        private readonly Dictionary<string, string> _toolCalls = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> _shown = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<Guid, string> _names = new Dictionary<Guid, string>();
        private readonly List<string> _typedHere = [];
        private DateTimeOffset? _turnStartedAt;
        private bool _lineOpen;

        // A message typed here, whose event isn't shown again.
        public void TypedHere(string text)
        {
            _typedHere.Add(text);
        }

        public async Task PrintAsync(string type, Wire.ChatEvent chatEvent, CancellationToken ct)
        {
            JsonElement body = chatEvent.Event;
            switch (type)
            {
                case "message-sent":
                    Guid sender = body.GetProperty("sentBy").GetGuid();
                    string text = StringOf(body, "text") ?? string.Empty;
                    if (sender == me && _typedHere.Remove(text))
                    {
                        break;
                    }

                    string name = await NameOfAsync(sender, ct);
                    await LineAsync("› " + name + ": " + text);
                    break;
                case "message-proposed":
                    string proposer = await NameOfAsync(body.GetProperty("proposedBy").GetGuid(), ct);
                    await LineAsync("› " + proposer + " proposes: " + StringOf(body, "text"));
                    break;
                case "message-cancelled":
                    await LineAsync("  (a message was cancelled before the agent read it)");
                    break;
                case "turn-started":
                    _turnStartedAt = chatEvent.At;
                    break;
                case "agent-update":
                    await PrintUpdateAsync(body.GetProperty("update"));
                    break;
                case "turn-ended":
                    string? took = _turnStartedAt is DateTimeOffset started ? " · " + Duration(chatEvent.At - started) : null;
                    await LineAsync("── " + Ending(StringOf(body, "stopReason"), StringOf(body, "failure")) + took + " ──");
                    _turnStartedAt = null;
                    break;
                case "checkpoint-saved":
                    await LineAsync(string.Create(CultureInfo.InvariantCulture, $"  (files saved as checkpoint {body.GetProperty("number").GetInt32()})"));
                    break;
                case "checkpoint-failed":
                    await LineAsync("  (saving the files failed: " + StringOf(body, "failure") + ")");
                    break;
                case "agent-restarted":
                    bool remembers = body.GetProperty("remembers").GetBoolean();
                    await LineAsync(remembers ? "  (the agent started again, and remembers the conversation)" : "  (the agent started again, without the earlier conversation)");
                    break;
                default:
                    // message-steered, and kinds a newer host adds: nothing to show.
                    break;
            }
        }

        private async Task PrintUpdateAsync(JsonElement update)
        {
            string? kind = StringOf(update, "sessionUpdate");
            string? id = StringOf(update, "toolCallId");
            string? title = StringOf(update, "title");
            if (id is not null && title is not null)
            {
                _toolCalls[id] = title;
            }

            if (kind is "agent_message_chunk")
            {
                bool hasContent = update.TryGetProperty("content", out JsonElement content);
                string? text = hasContent && StringOf(content, "type") is "text" ? StringOf(content, "text") : null;
                if (!string.IsNullOrEmpty(text))
                {
                    await output.WriteAsync(text);
                    _lineOpen = text[^1] != '\n';
                }
            }
            else if (kind is "tool_call" or "tool_call_update" && id is not null)
            {
                // A tool call is shown once, when it's named or starts running, whichever comes first:
                // some agents name it only in an update after the call.
                string? status = StringOf(update, "status");
                bool named = kind is "tool_call_update" && title is not null;
                bool started = status is "in_progress" or "completed" or "failed";
                if ((named || started) && _shown.Add(id))
                {
                    await LineAsync("  ▸ " + (_toolCalls.GetValueOrDefault(id) ?? "a tool"));
                }

                if (status is "failed")
                {
                    await LineAsync("  ✗ " + (_toolCalls.GetValueOrDefault(id) ?? "a tool") + " failed");
                }
            }
        }

        // A whole line, after ending the agent's open one.
        private async Task LineAsync(string line)
        {
            if (_lineOpen)
            {
                await output.WriteLineAsync();
                _lineOpen = false;
            }

            await output.WriteLineAsync(line);
        }

        private async Task<string> NameOfAsync(Guid person, CancellationToken ct)
        {
            if (person == me)
            {
                return "You";
            }

            string? known = _names.GetValueOrDefault(person);
            if (known is not null)
            {
                return known;
            }

            IReadOnlyList<Wire.Person> found = await api.GetAsync(PeoplePath([person]), CliJsonContext.Default.IReadOnlyListPerson, ct);
            string name = found is [Wire.Person named] ? named.Name : "Someone";
            _names[person] = name;
            return name;
        }

        private static string Ending(string? stopReason, string? failure)
        {
            return stopReason switch
            {
                "end_turn" => "done",
                "cancelled" => "stopped",
                "max_tokens" => "stopped at the model's output limit",
                "max_turn_requests" => "stopped at the turn's limit of model requests",
                "refusal" => "the model refused",
                "failed" => "failed: " + failure,
                _ => "ended: " + stopReason,
            };
        }

        private static string Duration(TimeSpan took)
        {
            return took.TotalMinutes < 1 ? string.Create(CultureInfo.InvariantCulture, $"{(int)took.TotalSeconds}s")
                : took.TotalHours < 1 ? string.Create(CultureInfo.InvariantCulture, $"{(int)took.TotalMinutes}m {took.Seconds}s")
                : string.Create(CultureInfo.InvariantCulture, $"{(int)took.TotalHours}h {took.Minutes}m");
        }

        // A string property, or null when it's missing or not a string: updates come from many agents.
        private static string? StringOf(JsonElement element, string property)
        {
            bool has = element.TryGetProperty(property, out JsonElement value);
            return has && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }
    }
}
