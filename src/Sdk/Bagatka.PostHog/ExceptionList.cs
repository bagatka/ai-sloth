using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Bagatka.PostHog;

// An exception and its inner ones as error tracking's $exception_list: outermost first, each with its
// stack's frames, innermost first, as .NET lists them. Methods are named through DiagnosticMethodInfo,
// which works under Native AOT, where reflection over methods doesn't; an async method's frame names
// the method, not its compiler-made state machine. Files and lines come where the build shipped symbols.
internal static partial class ExceptionList
{
    private const int MaxExceptions = 10;
    private const int MaxFrames = 50;

    public static JsonArray From(Exception exception)
    {
        JsonArray list = new JsonArray();
        Stack<Exception> pending = new Stack<Exception>();
        HashSet<Exception> seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        pending.Push(exception);
        while (pending.Count > 0 && list.Count < MaxExceptions)
        {
            Exception current = pending.Pop();
            if (!seen.Add(current))
            {
                continue;
            }

            list.Add((JsonNode)new JsonObject
            {
                ["type"] = current.GetType().FullName ?? current.GetType().Name,
                ["value"] = current.Message,
                ["mechanism"] = new JsonObject { ["type"] = "generic", ["handled"] = true, ["synthetic"] = false },
                ["stacktrace"] = new JsonObject { ["type"] = "raw", ["frames"] = Frames(current) },
            });
            if (current is AggregateException aggregate)
            {
                for (int i = aggregate.InnerExceptions.Count - 1; i >= 0; i--)
                {
                    pending.Push(aggregate.InnerExceptions[i]);
                }
            }
            else if (current.InnerException is not null)
            {
                pending.Push(current.InnerException);
            }
        }

        return list;
    }

    private static JsonArray Frames(Exception exception)
    {
        JsonArray frames = new JsonArray();
        foreach (StackFrame frame in new StackTrace(exception, fNeedFileInfo: true).GetFrames())
        {
            DiagnosticMethodInfo? method = DiagnosticMethodInfo.Create(frame);
            string module = method?.DeclaringTypeName ?? string.Empty;
            string function = method?.Name ?? string.Empty;
            Match stateMachine = AsyncStateMachine().Match(module);
            if (string.Equals(function, "MoveNext", StringComparison.Ordinal) && stateMachine.Success)
            {
                module = stateMachine.Groups["type"].Value;
                function = stateMachine.Groups["method"].Value;
            }

            // What awaiting and rethrowing add, which .NET's own stack traces hide too.
            if (module.StartsWith("System.Runtime.CompilerServices.", StringComparison.Ordinal)
                || module.StartsWith("System.Runtime.ExceptionServices.", StringComparison.Ordinal))
            {
                continue;
            }

            JsonObject item = new JsonObject
            {
                ["platform"] = "custom",
                ["lang"] = "dotnet",
                ["function"] = function,
                ["module"] = module,
                ["in_app"] = !module.StartsWith("System.", StringComparison.Ordinal) && !module.StartsWith("Microsoft.", StringComparison.Ordinal),
            };
            string? path = frame.GetFileName();
            if (path is not null)
            {
                item["filename"] = Path.GetFileName(path);
                item["abs_path"] = path;
            }

            int line = frame.GetFileLineNumber();
            if (line > 0)
            {
                item["lineno"] = line;
                item["colno"] = frame.GetFileColumnNumber();
            }

            frames.Add((JsonNode)item);
            if (frames.Count == MaxFrames)
            {
                break;
            }
        }

        return frames;
    }

    // An async method's state machine, such as "Namespace.Type+<SendAsync>d__12", or "…d__12`1" for a
    // generic method.
    [GeneratedRegex(@"^(?<type>.+)\+<(?<method>[^>]+)>d__\d+(?:`\d+)?$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex AsyncStateMachine();
}
