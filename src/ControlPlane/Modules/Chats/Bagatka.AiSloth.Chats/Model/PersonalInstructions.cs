using System;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats.Model;

// A person's own instructions for every agent of the chats they start (AgentInstructions).
internal sealed class PersonalInstructions
{
    // Used by Write and by EF: parameter names match property names.
    private PersonalInstructions(UserId personId, string text, DateTimeOffset updatedAt)
    {
        PersonId = personId;
        Text = text;
        UpdatedAt = updatedAt;
    }

    public UserId PersonId { get; private set; }

    public string Text { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    // The text is checked by AgentInstructions.Check.
    public static PersonalInstructions Write(UserId personId, string text, TimeProvider time)
    {
        return new PersonalInstructions(personId, text, time.GetUtcNow());
    }

    public void Rewrite(string text, TimeProvider time)
    {
        Text = text;
        UpdatedAt = time.GetUtcNow();
    }
}
