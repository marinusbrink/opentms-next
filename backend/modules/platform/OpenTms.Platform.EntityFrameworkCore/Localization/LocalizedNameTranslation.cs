using System.Collections.Generic;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using OpenTms.Platform.Localization;

namespace OpenTms.Platform.EntityFrameworkCore.Localization;

/// <summary>
/// Translates <see cref="LocalizedNameQuery.Text"/> to the provider's JSON text extraction:
/// <c>jsonb_extract_path_text(col, culture)</c> on PostgreSQL, <c>json_extract(col, '$.' || culture)</c> on SQLite
/// (the unit-test provider). An EF Core method-call translator plugin, so no raw SQL and no DbFunction with an
/// unmapped parameter type.
/// </summary>
public sealed class LocalizedNameMethodCallTranslator : IMethodCallTranslator
{
    private static readonly MethodInfo TextMethod = typeof(LocalizedNameQuery).GetMethod(nameof(LocalizedNameQuery.Text))!;

    private readonly System.IServiceProvider _services;

    /* EF Core's RelationalMethodCallTranslatorProvider enumerates the plugins while the context's
     * dependencies are being built, before the model exists; ISqlExpressionFactory depends on the model,
     * so both services are resolved per translation, never in the constructor. */
    public LocalizedNameMethodCallTranslator(System.IServiceProvider services)
    {
        _services = services;
    }

    public SqlExpression? Translate(
        SqlExpression? instance,
        MethodInfo method,
        IReadOnlyList<SqlExpression> arguments,
        IDiagnosticsLogger<DbLoggerCategory.Query> logger)
    {
        if (method != TextMethod)
        {
            return null;
        }

        var sqlExpressionFactory = (ISqlExpressionFactory)_services.GetService(typeof(ISqlExpressionFactory))!;
        var typeMappingSource = (IRelationalTypeMappingSource)_services.GetService(typeof(IRelationalTypeMappingSource))!;

        var stringMapping = typeMappingSource.FindMapping(typeof(string))!;
        var column = arguments[0];
        var culture = sqlExpressionFactory.ApplyTypeMapping(arguments[1], stringMapping);

        var isPostgreSql = typeMappingSource.GetType().Name.StartsWith("Npgsql", System.StringComparison.Ordinal);
        if (isPostgreSql)
        {
            /* The result is null whenever the map lacks the culture, so a non-null column must not
             * make EF Core infer a non-null result (it would drop the COALESCE of the fallback). */
            return sqlExpressionFactory.Function(
                "jsonb_extract_path_text",
                [column, culture],
                nullable: true,
                argumentsPropagateNullability: [false, false],
                typeof(string),
                stringMapping);
        }

        /* SQLite: json_extract(col, '$.' || culture). */
        var path = sqlExpressionFactory.Add(
            sqlExpressionFactory.Constant("$.", stringMapping),
            culture,
            stringMapping);

        return sqlExpressionFactory.Function(
            "json_extract",
            [column, path],
            nullable: true,
            argumentsPropagateNullability: [false, false],
            typeof(string),
            stringMapping);
    }
}

public sealed class LocalizedNameMethodCallTranslatorPlugin : IMethodCallTranslatorPlugin
{
    public LocalizedNameMethodCallTranslatorPlugin(System.IServiceProvider services)
    {
        Translators = [new LocalizedNameMethodCallTranslator(services)];
    }

    public IEnumerable<IMethodCallTranslator> Translators { get; }
}

/// <summary>Registers the translator plugin in EF Core's internal service provider.</summary>
public sealed class LocalizedNameOptionsExtension : IDbContextOptionsExtension
{
    private ExtensionInfo? _info;

    public DbContextOptionsExtensionInfo Info => _info ??= new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
    {
        /* EF Core registers translator plugins as scoped services (they sit next to the model); the
         * services builder applies the lifetime EF expects. */
        new EntityFrameworkRelationalServicesBuilder(services)
            .TryAdd<IMethodCallTranslatorPlugin, LocalizedNameMethodCallTranslatorPlugin>();
    }

    public void Validate(IDbContextOptions options)
    {
    }

    private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
    {
        public ExtensionInfo(IDbContextOptionsExtension extension)
            : base(extension)
        {
        }

        public override bool IsDatabaseProvider => false;

        public override string LogFragment => "using LocalizedName translation ";

        public override int GetServiceProviderHashCode() => 0;

        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;

        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
        {
            debugInfo["Platform:LocalizedName"] = "1";
        }
    }
}

public static class LocalizedNameDbContextOptionsBuilderExtensions
{
    /// <summary>Enables the SQL translation of <see cref="LocalizedNameQuery"/> for this context (any provider).</summary>
    public static DbContextOptionsBuilder UseLocalizedNameTranslation(this DbContextOptionsBuilder builder)
    {
        var extension = builder.Options.FindExtension<LocalizedNameOptionsExtension>() ?? new LocalizedNameOptionsExtension();
        ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(extension);
        return builder;
    }

    public static DbContextOptionsBuilder<TContext> UseLocalizedNameTranslation<TContext>(this DbContextOptionsBuilder<TContext> builder)
        where TContext : DbContext
        => (DbContextOptionsBuilder<TContext>)UseLocalizedNameTranslation((DbContextOptionsBuilder)builder);
}
