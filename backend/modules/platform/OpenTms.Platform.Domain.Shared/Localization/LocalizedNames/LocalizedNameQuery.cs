namespace OpenTms.Platform.Localization;

/// <summary>
/// Query helpers for <see cref="LocalizedName"/> that EF Core translates to SQL
/// (<c>jsonb_extract_path_text</c> on PostgreSQL, <c>json_extract</c> on SQLite) when the Platform
/// EF Core module's translation is enabled on the DbContext. Used in LINQ to sort and search on a
/// resolved name: <c>LocalizedNameQuery.Text(e.Name, culture) ?? LocalizedNameQuery.Text(e.Name, LocalizedName.DefaultCulture)</c>.
/// Called on objects in memory, the methods simply read the map.
/// </summary>
public static class LocalizedNameQuery
{
    /// <summary>The text stored for <paramref name="culture"/>, or null when the name has none.</summary>
    public static string? Text(LocalizedName? name, string culture) => name?.TextFor(culture);
}
