using System;
using System.Text;

namespace Bagatka.ModelAccess;

/// <summary>
/// What pays for a vendor's models, which the caller keeps sealed: an API key or a plan's token; for a
/// plan that is renewed, its refresh token and when its token expires; and the vendor's account the
/// sign-in chose, when calls must name it. Its text form leaves the tokens out.
/// </summary>
/// <param name="Token">The API key, or the plan's access token.</param>
/// <param name="RefreshToken">What renews a plan's access token (<see cref="ModelVendors.RenewAsync"/>); null for one never renewed.</param>
/// <param name="ExpiresAt">When the access token expires; null for one that doesn't.</param>
/// <param name="VendorAccount">The vendor's account the sign-in chose, such as a ChatGPT workspace's.</param>
public sealed record ModelCredential(string Token, string? RefreshToken = null, DateTimeOffset? ExpiresAt = null, string? VendorAccount = null)
{
    private static readonly TimeSpan RenewalMargin = TimeSpan.FromMinutes(5);

    /// <summary>Whether to renew it before it is used: shortly before its token expires.</summary>
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
