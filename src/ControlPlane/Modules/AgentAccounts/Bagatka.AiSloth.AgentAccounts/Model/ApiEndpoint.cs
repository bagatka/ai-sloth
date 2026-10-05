using System;
using System.Globalization;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.AgentAccounts.Model;

// An API key's endpoint when it isn't the vendor's own: an absolute http or https base URL, without
// credentials, query, or fragment. Which networks the model gateway may reach is its own policy.
internal sealed record ApiEndpoint
{
    public const int MaxLength = 2048;

    private ApiEndpoint(Uri value)
    {
        Value = value;
    }

    public Uri Value { get; }

    public static Result<ApiEndpoint> Parse(Uri? input)
    {
        bool valid = input is not null
            && input.IsAbsoluteUri
            && input.Scheme is "https" or "http"
            && input.UserInfo.Length == 0
            && input.Query.Length == 0
            && input.Fragment.Length == 0
            && input.AbsoluteUri.Length <= MaxLength;
        if (!valid)
        {
            string message = string.Create(CultureInfo.InvariantCulture, $"Must be an http or https base URL of at most {MaxLength} characters, without credentials, query, or fragment.");
            return new Result<ApiEndpoint>(Error.Validation("endpoint", message));
        }

        return new Result<ApiEndpoint>(new ApiEndpoint(input!));
    }
}
