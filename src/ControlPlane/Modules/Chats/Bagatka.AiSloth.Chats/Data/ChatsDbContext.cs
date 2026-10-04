using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Chats.Data;

internal sealed class ChatsDbContext(DbContextOptions<ChatsDbContext> options) : DbContext(options)
{
    public const string Schema = "chats";

    public DbSet<Chat> Chats => Set<Chat>();

    public DbSet<Message> Messages => Set<Message>();

    public DbSet<StoredEvent> Events => Set<StoredEvent>();

    public DbSet<ChatSender> Senders => Set<ChatSender>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new ChatConfiguration());
        modelBuilder.ApplyConfiguration(new MessageConfiguration());
        modelBuilder.ApplyConfiguration(new StoredEventConfiguration());
        modelBuilder.Entity<ChatSender>().HasKey(sender => new { sender.ChatId, sender.UserId });
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<AgentAccountId>().HaveConversion<TypedIdConverter<AgentAccountId>>();
        configurationBuilder.Properties<ChatId>().HaveConversion<TypedIdConverter<ChatId>>();
        configurationBuilder.Properties<MessageId>().HaveConversion<TypedIdConverter<MessageId>>();
        configurationBuilder.Properties<NookId>().HaveConversion<TypedIdConverter<NookId>>();
        configurationBuilder.Properties<ProcessId>().HaveConversion<TypedIdConverter<ProcessId>>();
        configurationBuilder.Properties<WorkspaceId>().HaveConversion<TypedIdConverter<WorkspaceId>>();
        configurationBuilder.Properties<UserId>().HaveConversion<TypedIdConverter<UserId>>();
        configurationBuilder.Properties<MessageState>().HaveConversion<string>().HaveMaxLength(StoredEnums.MaxLength);
    }
}
