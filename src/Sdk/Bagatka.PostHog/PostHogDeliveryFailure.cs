using System;

namespace Bagatka.PostHog;

/// <summary>
/// Events a <see cref="PostHogClient"/> lost, and why.
/// </summary>
/// <param name="Events">How many events were lost.</param>
/// <param name="Reason">
/// Why: <c>queue_full</c> when they were captured faster than they could be sent, <c>refused</c> when
/// PostHog answered with an error that sending again wouldn't change, such as a wrong token, or
/// <c>unreachable</c> when PostHog stayed out of reach or kept failing.
/// </param>
/// <param name="StatusCode">PostHog's HTTP status, when it answered.</param>
/// <param name="Exception">What failed, when sending threw.</param>
public sealed record PostHogDeliveryFailure(int Events, string Reason, int? StatusCode, Exception? Exception);
