namespace Bagatka.AiSloth.Users.Contracts;

/// <summary>
/// A new session for a person who signed in. Its token goes in each call's <c>Authorization: Bearer</c>
/// header; it is shown only here. Its text form leaves the token out.
/// </summary>
/// <param name="Id">The session.</param>
/// <param name="Token">The session's secret.</param>
/// <param name="Person">Who signed in.</param>
/// <param name="IsNew">Whether they signed up just now.</param>
public sealed record StartedSession(SessionId Id, string Token, UserSummary Person, bool IsNew)
{
    /// <inheritdoc />
    public override string ToString()
    {
        return "StartedSession { Id = " + Id.Value + ", Token = ***, Person = " + Person + ", IsNew = " + IsNew + " }";
    }
}
