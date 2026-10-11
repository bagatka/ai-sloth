using System;
using System.Text;

namespace Bagatka.AiSloth.AgentAccounts.Model;

// What pays for an account's calls, kept sealed as its secret: an API key or a plan's token; for a plan
// this host renews, its refresh token and when its token expires; and the vendor's account the sign-in
// chose, when calls name it. Its text form leaves the tokens out.
internal sealed record Credential(string Token, string? RefreshToken = null, DateTimeOffset? ExpiresAt = null, string? VendorAccount = null)
{
    private static readonly TimeSpan RenewalMargin = TimeSpan.FromMinutes(5);

    // Whether to renew it before it is used: shortly before its token expires.
    public bool RenewalDue(DateTimeOffset now)
    {
        return RefreshToken is not null && ExpiresAt is DateTimeOffset expiresAt && now >= expiresAt - RenewalMargin;
    }

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("ExpiresAt = ").Append(ExpiresAt).Append(", VendorAccount = ").Append(VendorAccount).Append(", Token = ***");
        return true;
    }
}
