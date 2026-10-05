using System;
using System.Globalization;
using Bagatka.AiSloth.Secrets.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;

namespace Bagatka.AiSloth.Secrets.Model;

// A workspace's environment variable for every process in its nooks. The value is kept only sealed.
internal sealed class Secret
{
    public const int MaxValueLength = 16384;

    // Bounds what every process start carries.
    public const int MaxPerWorkspace = 100;

    // Used by Add and by EF: parameter names match property names.
    private Secret(SecretId id, WorkspaceId workspaceId, SecretName name, UserId setBy, DateTimeOffset setAt)
    {
        Id = id;
        WorkspaceId = workspaceId;
        Name = name;
        SetBy = setBy;
        SetAt = setAt;
    }

    public SecretId Id { get; private set; }

    public WorkspaceId WorkspaceId { get; private set; }

    public SecretName Name { get; private set; }

    public UserId SetBy { get; private set; }

    public DateTimeOffset SetAt { get; private set; }

    public byte[] SealedValue { get; private set; } = [];

    public static Result<Secret> Add(WorkspaceId workspaceId, SecretName name, string? value, UserId setBy, SecretBox box, TimeProvider time)
    {
        Secret secret = new Secret(SecretId.New(), workspaceId, name, setBy, time.GetUtcNow());
        Result sealedValue = secret.Seal(value, box);
        if (sealedValue.Failed)
        {
            return new Result<Secret>(sealedValue.Error);
        }

        return new Result<Secret>(secret);
    }

    public Result Replace(string? value, UserId setBy, SecretBox box, TimeProvider time)
    {
        Result sealedValue = Seal(value, box);
        if (sealedValue.Failed)
        {
            return sealedValue;
        }

        SetBy = setBy;
        SetAt = time.GetUtcNow();
        return new Result(new Success());
    }

    public string Open(SecretBox box)
    {
        return box.Open(SealedValue, Id.Value);
    }

    public SecretSummary ToSummary()
    {
        return new SecretSummary(Id, WorkspaceId, Name.Value, SetBy, SetAt);
    }

    // Kept exactly as given: an environment value can hold anything but NUL.
    private Result Seal(string? value, SecretBox box)
    {
        bool valid = value is { Length: > 0 and <= MaxValueLength } && !value.Contains('\0', StringComparison.Ordinal);
        if (!valid)
        {
            string message = string.Create(CultureInfo.InvariantCulture, $"Must be 1 to {MaxValueLength} characters, without NUL.");
            return new Result(Error.Validation("value", message));
        }

        SealedValue = box.Seal(value!, Id.Value);
        return new Result(new Success());
    }
}
