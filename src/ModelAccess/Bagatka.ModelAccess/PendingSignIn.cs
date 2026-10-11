using System;

namespace Bagatka.ModelAccess;

/// <summary>
/// A plan's sign-in as its vendor started it (<see cref="ModelVendors.StartSignInAsync"/>): where the
/// person goes, the code they enter there if any, how long it lasts, how often the vendor may be asked,
/// and the secret it continues with, which the caller keeps sealed until the sign-in ends.
/// </summary>
/// <param name="Url">The vendor's page.</param>
/// <param name="UserCode">What the person enters there, such as <c>WDJB-MJHT</c>; null when the page shows them a code to bring back instead.</param>
/// <param name="ExpiresIn">How long it can be continued.</param>
/// <param name="Interval">How long to wait between asking whether the person approved; zero when they bring back a code.</param>
/// <param name="Secret">What continuing it takes, such as a device code or a PKCE verifier. Never log it.</param>
public sealed record PendingSignIn(Uri Url, string? UserCode, TimeSpan ExpiresIn, TimeSpan Interval, string Secret)
{
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("Url = ").Append(Url.AbsoluteUri).Append(", UserCode = ").Append(UserCode).Append(", Secret = ***");
        return true;
    }
}
