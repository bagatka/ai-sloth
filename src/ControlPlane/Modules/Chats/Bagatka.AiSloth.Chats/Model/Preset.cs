using System;
using System.Globalization;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.Foundation;
using Bagatka.Harnesses;

namespace Bagatka.AiSloth.Chats.Model;

// A person's saved choice of how a chat runs (PresetSummary). Its harness, account, and model are
// checked as a chat's start checks them when it is saved; an account removed later only hides it.
internal sealed class Preset
{
    public const int MaxNameLength = 64;

    // Two or three are what people use; this bounds what one person keeps.
    public const int MaxPerPerson = 20;

    // Used by Save and by EF: parameter names match property names.
    private Preset(PresetId id, UserId ownerId, string name, string harness, AgentAccountId agentAccountId, string? model, DateTimeOffset addedAt)
    {
        Id = id;
        OwnerId = ownerId;
        Name = name;
        Harness = harness;
        AgentAccountId = agentAccountId;
        Model = model;
        AddedAt = addedAt;
    }

    public PresetId Id { get; private set; }

    public UserId OwnerId { get; private set; }

    public string Name { get; private set; }

    public string Harness { get; private set; }

    public AgentAccountId AgentAccountId { get; private set; }

    public string? Model { get; private set; }

    public DateTimeOffset AddedAt { get; private set; }

    // Without a name, it is named after its harness and model, cut to fit.
    public static Result<Preset> Save(UserId ownerId, string? name, HarnessProfile harness, AgentAccountId account, string? model, TimeProvider time)
    {
        string named = name ?? (model is null ? harness.Name : harness.Name + " · " + model);
        Result<string> checkedName = Checked(name is null && named.Length > MaxNameLength ? named[..MaxNameLength] : named);
        if (checkedName.Failed)
        {
            return new Result<Preset>(checkedName.Error);
        }

        return new Result<Preset>(new Preset(PresetId.New(), ownerId, checkedName.Output, harness.Id, account, model, time.GetUtcNow()));
    }

    public Result Rename(string name)
    {
        Result<string> checkedName = Checked(name);
        if (checkedName.Failed)
        {
            return new Result(checkedName.Error);
        }

        Name = checkedName.Output;
        return new Result(new Success());
    }

    public PresetSummary ToSummary()
    {
        return new PresetSummary(Id, Name, Harness, AgentAccountId, Model, AddedAt);
    }

    private static Result<string> Checked(string? name)
    {
        string trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length is 0 or > MaxNameLength)
        {
            string message = string.Create(CultureInfo.InvariantCulture, $"Must be 1 to {MaxNameLength} characters.");
            return new Result<string>(Error.Validation("name", message));
        }

        return new Result<string>(trimmed);
    }
}
