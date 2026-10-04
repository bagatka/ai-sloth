using System;
using System.Text.Json;
using Xunit;

namespace Bagatka.Foundation.Tests;

public sealed class UserIdTests
{
    [Fact]
    public void New_creates_time_ordered_version_7_ids()
    {
        UserId id = UserId.New();

        Assert.Equal(7, id.Value.Version);
    }

    [Fact]
    public void Json_carries_the_id_as_a_guid_string()
    {
        UserId id = UserId.From(new Guid("0199a7a4-5b9e-7c3d-8e2f-1a2b3c4d5e6f"));

        string json = JsonSerializer.Serialize(id, JsonSerializerOptions.Default);
        UserId roundTripped = JsonSerializer.Deserialize<UserId>(json, JsonSerializerOptions.Default);

        Assert.Equal("\"0199a7a4-5b9e-7c3d-8e2f-1a2b3c4d5e6f\"", json);
        Assert.Equal(id, roundTripped);
    }
}
