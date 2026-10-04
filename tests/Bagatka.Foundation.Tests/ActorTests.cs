using System;
using Xunit;

namespace Bagatka.Foundation.Tests;

public sealed class ActorTests
{
    private static readonly UserId Alice = UserId.From(new Guid("0199a7a4-5b9e-7c3d-8e2f-1a2b3c4d5e6f"));
    private static readonly UserId Bob = UserId.From(new Guid("0199a7a4-5b9e-7c3d-8e2f-6f5e4d3c2b1a"));

    [Fact]
    public void ToLogValue_names_each_kind_of_actor()
    {
        Assert.Equal("user:0199a7a4-5b9e-7c3d-8e2f-1a2b3c4d5e6f", Actor.ForUser(Alice).ToLogValue());
        Assert.Equal("system:sandboxes.reconciler", Actor.ForSystem("sandboxes.reconciler").ToLogValue());
        Assert.Equal("anonymous", Actor.Anonymous.ToLogValue());
    }

    [Fact]
    public void Is_matches_only_the_same_user()
    {
        Actor actor = Actor.ForUser(Alice);

        Assert.True(actor.Is(Alice));
        Assert.False(actor.Is(Bob));
        Assert.False(Actor.ForSystem("sandboxes.reconciler").Is(Alice));
    }

    [Fact]
    public void ForSystem_rejects_a_blank_name()
    {
        Assert.Throws<ArgumentException>(() => Actor.ForSystem(" "));
    }
}
