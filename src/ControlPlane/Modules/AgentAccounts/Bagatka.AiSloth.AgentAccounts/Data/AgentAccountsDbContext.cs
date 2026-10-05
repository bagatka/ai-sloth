using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.AgentAccounts.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.AgentAccounts.Data;

internal sealed class AgentAccountsDbContext(DbContextOptions<AgentAccountsDbContext> options) : DbContext(options)
{
    public const string Schema = "agent_accounts";

    public DbSet<AgentAccount> Accounts => Set<AgentAccount>();

    public DbSet<SignIn> SignIns => Set<SignIn>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new AgentAccountConfiguration());
        modelBuilder.ApplyConfiguration(new SignInConfiguration());
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<AgentAccountId>().HaveConversion<TypedIdConverter<AgentAccountId>>();
        configurationBuilder.Properties<SignInId>().HaveConversion<TypedIdConverter<SignInId>>();
        configurationBuilder.Properties<WorkspaceId>().HaveConversion<TypedIdConverter<WorkspaceId>>();
        configurationBuilder.Properties<UserId>().HaveConversion<TypedIdConverter<UserId>>();
        configurationBuilder.Properties<AgentAccountKind>().HaveConversion<string>().HaveMaxLength(StoredEnums.MaxLength);
        configurationBuilder.Properties<AgentAccountName>()
            .HaveConversion<AgentAccountNameConverter>()
            .HaveMaxLength(AgentAccountName.MaxLength);
        configurationBuilder.Properties<ApiEndpoint>()
            .HaveConversion<ApiEndpointConverter>()
            .HaveMaxLength(ApiEndpoint.MaxLength);
    }
}
