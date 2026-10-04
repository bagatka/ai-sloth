using System.Globalization;

namespace Bagatka.Foundation;

/// <summary>
/// Who performs a call: a user, a named system process, or an anonymous caller. It carries identity
/// only, never permissions; the module that owns the data decides what the actor may do. Switch over
/// the cases exhaustively, so adding a kind of actor breaks every authorization rule that ignores it.
/// </summary>
public union Actor(UserActor, SystemActor, AnonymousActor)
{
    /// <summary>A caller that is not authenticated.</summary>
    public static Actor Anonymous { get; } = new Actor(new AnonymousActor());

    /// <summary>A call made on behalf of a user.</summary>
    public static Actor ForUser(UserId userId)
    {
        return new Actor(new UserActor(userId));
    }

    /// <summary>A call made by a background process, named <c>&lt;module&gt;.&lt;process&gt;</c>.</summary>
    public static Actor ForSystem(string name)
    {
        return new Actor(new SystemActor(name));
    }

    /// <summary>Returns <see langword="true"/> when this actor is the given user.</summary>
    public bool Is(UserId userId)
    {
        return Value is UserActor user && user.UserId == userId;
    }

    /// <summary>
    /// The only way an actor appears in logs: <c>user:&lt;id&gt;</c>, <c>system:&lt;name&gt;</c>, or
    /// <c>anonymous</c>.
    /// </summary>
    public string ToLogValue()
    {
        return this switch
        {
            UserActor user => string.Create(CultureInfo.InvariantCulture, $"user:{user.UserId.Value}"),
            SystemActor system => string.Create(CultureInfo.InvariantCulture, $"system:{system.Name}"),
            AnonymousActor => "anonymous",
        };
    }
}
