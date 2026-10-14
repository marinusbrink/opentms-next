using System;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenTms.Platform.Tenants;

/// <summary>
/// Names a tenant's database and login role: <c>t_&lt;slug&gt;_&lt;the whole tenant id, 32 hex&gt;</c>. Unique by
/// the id part, stable across provisioning retries, readable in psql, and only ever <c>[a-z0-9_]</c> so it
/// is safe as a quoted identifier. The drop job refuses any name that does not match <see cref="IsGenerated"/>.
/// The whole id, not a prefix: ABP's sequential GUIDs start with a timestamp, so a prefix is shared by every tenant
/// created within the same minute and two tenants with the same slug would get one database.
/// </summary>
public static partial class TenantDatabaseNaming
{
    /// <summary>PostgreSQL's identifier limit (NAMEDATALEN - 1).</summary>
    public const int MaxLength = 63;

    private const string Prefix = "t_";

    public static string For(string tenantName, Guid tenantId)
    {
        var suffix = "_" + tenantId.ToString("N");
        var slug = Slug(tenantName);
        var room = MaxLength - Prefix.Length - suffix.Length;
        if (slug.Length > room)
        {
            slug = slug[..room].TrimEnd('_');
        }

        return Prefix + (slug.Length == 0 ? "tenant" : slug) + suffix;
    }

    public static bool IsGenerated(string? name) =>
        name != null && name.Length <= MaxLength && GeneratedPattern().IsMatch(name);

    private static string Slug(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value.Trim().ToLowerInvariant())
        {
            builder.Append(c is >= 'a' and <= 'z' or >= '0' and <= '9' ? c : '_');
        }

        return CollapseUnderscores().Replace(builder.ToString(), "_").Trim('_');
    }

    /* \z, not $: in .NET $ also matches before a final newline. */
    [GeneratedRegex(@"^t_[a-z0-9_]*_[0-9a-f]{32}\z")]
    private static partial Regex GeneratedPattern();

    [GeneratedRegex("_+")]
    private static partial Regex CollapseUnderscores();
}
