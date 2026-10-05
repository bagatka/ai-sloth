using System;
using System.Collections.Generic;
using System.Text;

namespace Bagatka.AiSloth.AgentAccounts.Contracts;

/// <summary>
/// Where the model gateway forwards an agent's calls, and the headers that pay for them. A call's path
/// follows the endpoint's base URL, as the vendor's own clients build it: <c>/responses</c> after
/// <c>https://api.openai.com/v1</c>, <c>/v1/messages</c> after <c>https://api.anthropic.com</c>. Its
/// text form leaves the headers out.
/// </summary>
/// <param name="Url">The API's base URL.</param>
/// <param name="Headers">The headers to add to each call, such as <c>Authorization</c>; they hold secrets.</param>
public sealed record ModelEndpoint(Uri Url, IReadOnlyDictionary<string, string> Headers)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Url = ").Append(Url.AbsoluteUri).Append(", Headers = ***");
        return true;
    }
}
