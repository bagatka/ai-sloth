using System;

namespace Bagatka.ModelAccess;

/// <summary>Where a plan's sign-in stands (<see cref="ModelVendors.ContinueSignInAsync"/>).</summary>
/// <param name="Credential">The plan's credential once the person approved; null while waiting for them.</param>
/// <param name="Interval">How long to wait before asking again, when the vendor asked for longer.</param>
public sealed record SignInStep(ModelCredential? Credential, TimeSpan? Interval = null);
