using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Bagatka.Analyzers.Tests;

public sealed class StatementShapeTests
{
    [Fact]
    public async Task An_await_is_its_statements_whole_expression()
    {
        const string sample = """
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;

            internal sealed class Sample
            {
                private int _count;

                private async Task<int> StandAloneAsync(IAsyncEnumerator<int> items)
                {
                    await Task.Yield();
                    int count = await CountAsync();
                    _count = await CountAsync();
                    while (await items.MoveNextAsync())
                    {
                        count += items.Current;
                    }

                    Func<Task<int>> later = async () => await CountAsync();
                    return await CountAsync();
                }

                private async Task<int> NestedAsync()
                {
                    if (await ReadyAsync()) // BAG0001
                    {
                        return 1;
                    }

                    int doubled = Twice(await CountAsync()); // BAG0001
                    int plusOne = await CountAsync() + 1; // BAG0001
                    Func<Task<string>> text = async () => (await CountAsync()).ToString(System.Globalization.CultureInfo.InvariantCulture); // BAG0001
                    return (await ReadyAsync()) ? doubled : plusOne; // BAG0001
                }

                private async Task<bool> SomethingNestedInALoopConditionAsync()
                {
                    while (Twice(await CountAsync()) > 2) // BAG0001
                    {
                        return true;
                    }

                    return false;
                }

                private static int Twice(int value) => value * 2;

                private Task<int> CountAsync() => Task.FromResult(_count);

                private static Task<bool> ReadyAsync() => Task.FromResult(true);
            }
            """;

        await Analysis.VerifyAsync(sample, OutputKind.DynamicallyLinkedLibrary);
    }

    [Fact]
    public async Task A_throw_is_its_own_statement()
    {
        const string sample = """
            using System;

            internal static class Sample
            {
                public static string Required(string? value)
                {
                    if (value is null)
                    {
                        throw new ArgumentNullException(nameof(value));
                    }

                    return value;
                }

                public static string Kind(int code) => code switch
                {
                    0 => "none",
                    _ => throw new ArgumentOutOfRangeException(nameof(code)),
                };

                public static string NotYet() => throw new NotSupportedException();

                public static string Coalesced(string? value) => value ?? throw new ArgumentNullException(nameof(value)); // BAG0008

                public static string Conditional(string value) => value.Length > 0 ? value : throw new ArgumentException("Empty.", nameof(value)); // BAG0008
            }
            """;

        await Analysis.VerifyAsync(sample, OutputKind.DynamicallyLinkedLibrary);
    }

    [Fact]
    public async Task A_call_with_an_out_argument_is_its_statements_whole_expression()
    {
        const string sample = """
            using System;
            using System.Collections.Generic;
            using System.Globalization;

            internal static class Sample
            {
                private static Guid? StandAlone(string text, Queue<int> queue)
                {
                    bool parsed = Guid.TryParse(text, CultureInfo.InvariantCulture, out Guid id);
                    while (queue.TryDequeue(out int item))
                    {
                        Console.WriteLine(item);
                    }

                    return parsed ? id : null;
                }

                private static Guid? Nested(string text)
                {
                    if (Guid.TryParse(text, CultureInfo.InvariantCulture, out Guid id)) // BAG0002
                    {
                        return id;
                    }

                    return Guid.TryParse(text.Trim(), CultureInfo.InvariantCulture, out Guid trimmed) ? trimmed : null; // BAG0002
                }
            }
            """;

        await Analysis.VerifyAsync(sample, OutputKind.DynamicallyLinkedLibrary);
    }
}
