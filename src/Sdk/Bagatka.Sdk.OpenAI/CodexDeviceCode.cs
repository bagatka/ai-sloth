using System;
using System.Text;

namespace Bagatka.Sdk.OpenAI;

/// <summary>
/// A code the person enters at OpenAI to sign Codex in to their ChatGPT plan (<see cref="CodexSignInClient.StartAsync"/>).
/// Its text form leaves the device's ID out.
/// </summary>
/// <param name="DeviceAuthId">Identifies the sign-in to OpenAI when asking whether the person approved; kept sealed.</param>
/// <param name="UserCode">What the person enters, such as <c>ABCD-1234</c>.</param>
/// <param name="VerificationUrl">Where they enter it.</param>
/// <param name="ExpiresIn">How long the code works: fifteen minutes.</param>
/// <param name="Interval">How often OpenAI may be asked.</param>
public sealed record CodexDeviceCode(string DeviceAuthId, string UserCode, Uri VerificationUrl, TimeSpan ExpiresIn, TimeSpan Interval)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("UserCode = ").Append(UserCode).Append(", DeviceAuthId = ***");
        return true;
    }
}
