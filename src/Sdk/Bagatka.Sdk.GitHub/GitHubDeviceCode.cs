using System.Text;
using System;

namespace Bagatka.Sdk.GitHub;

/// <summary>A device flow sign-in waiting for the person: they enter <paramref name="UserCode"/> at <paramref name="VerificationUri"/>.</summary>
/// <param name="DeviceCode">What the app polls with. A secret: whoever holds it gets the tokens once the person approves.</param>
/// <param name="UserCode">What the person enters, such as <c>WDJB-MJHT</c>.</param>
/// <param name="VerificationUri">Where they enter it.</param>
/// <param name="ExpiresIn">How long the codes work.</param>
/// <param name="Interval">How long to wait between polls.</param>
public sealed record GitHubDeviceCode(string DeviceCode, string UserCode, Uri VerificationUri, TimeSpan ExpiresIn, TimeSpan Interval)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("UserCode = ").Append(UserCode).Append(", DeviceCode = ***");
        return true;
    }
}
