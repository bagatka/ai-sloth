using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Bagatka.Foundation.Modules;

/// <summary>
/// Puts every module on the one PostgreSQL database the host has, each in a schema of its own
/// (PATTERNS.md, entry 13).
/// </summary>
public static partial class ModuleDatabaseRegistration
{
    /// <summary>
    /// The database every module and the instance lease share, registered once by the host: one pool
    /// of connections for all of them (<see cref="DatabaseSettings.ConnectionString"/>). Its queries'
    /// spans are named by what they do, such as <c>SELECT nooks.nooks</c>.
    /// </summary>
    public static IServiceCollection AddModuleDatabase(this IServiceCollection services, DatabaseSettings settings)
    {
        NpgsqlDataSourceBuilder database = new NpgsqlDataSourceBuilder(settings.ConnectionString);
        database.ConfigureTracing(tracing => tracing
            .ConfigureCommandSpanNameProvider(command => SpanName(command.CommandText))
            .ConfigureBatchSpanNameProvider(batch => string.Join(", ", batch.BatchCommands.Select((NpgsqlBatchCommand command) => SpanName(command.CommandText)).Distinct(StringComparer.Ordinal))));
        services.AddSingleton(_ => database.Build());
        return services;
    }

    /// <summary>
    /// Registers an <see cref="IDbContextFactory{TContext}"/> for a module's context on the shared
    /// database: every operation creates its own context and disposes it.
    /// <see cref="ModuleDatabases.MigrateAsync"/> applies its migrations.
    /// </summary>
    public static IServiceCollection AddModuleDbContext<TContext>(this IServiceCollection services, string schema)
        where TContext : DbContext
    {
        services.AddPooledDbContextFactory<TContext>((provider, options) => options.UseModuleDatabase(provider.GetRequiredService<NpgsqlDataSource>(), schema));
        services.AddSingleton(new ModuleDatabase(MigrateAsync<TContext>));
        return services;
    }

    /// <summary>
    /// The options every module context uses: PostgreSQL, snake_case names, and the migrations
    /// history table in the module's schema. Each module's design-time factory calls it too, without a
    /// database, so migrations are generated against the same model without connecting.
    /// </summary>
    public static DbContextOptionsBuilder UseModuleDatabase(this DbContextOptionsBuilder options, NpgsqlDataSource? database, string schema)
    {
        Action<NpgsqlDbContextOptionsBuilder> npgsql = builder => builder.MigrationsHistoryTable(HistoryRepository.DefaultTableName, schema);
        DbContextOptionsBuilder configured = database is null ? options.UseNpgsql(npgsql) : options.UseNpgsql(database, npgsql);
        return configured.UseSnakeCaseNamingConvention();
    }

    // A query's span name, as OpenTelemetry names database spans: its operation and the table it works
    // on, such as "UPDATE chats.chats", or for one it can't tell, the first word, such as "CREATE".
    private static string SpanName(string sql)
    {
        Match named = OperationAndTable().Match(sql);
        if (named.Success)
        {
            return named.Groups["operation"].Value + " " + named.Groups["table"].Value.Replace("\"", string.Empty, StringComparison.Ordinal);
        }

        Match first = FirstWord().Match(sql);
        return first.Success ? first.Value.ToUpperInvariant() : "postgresql";
    }

    // The statements EF Core writes: SELECT … FROM, DELETE FROM, INSERT INTO, or UPDATE, then a table
    // its schema names.
    [GeneratedRegex(@"^\s*(?:(?<operation>SELECT|DELETE)\b.*?\bFROM|(?<operation>INSERT)\s+INTO|(?<operation>UPDATE))\s+(?<table>""?\w+""?\.""?\w+""?)", RegexOptions.Singleline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex OperationAndTable();

    [GeneratedRegex(@"\w+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FirstWord();

    private static async Task MigrateAsync<TContext>(IServiceProvider services, CancellationToken ct)
        where TContext : DbContext
    {
        await using TContext db = await services.GetRequiredService<IDbContextFactory<TContext>>().CreateDbContextAsync(ct);
        await db.Database.MigrateAsync(ct);
    }
}
