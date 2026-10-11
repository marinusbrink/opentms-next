using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OpenTms.Platform.Localization;

namespace OpenTms.Platform.EntityFrameworkCore.Localization;

/// <summary>Maps a <see cref="LocalizedName"/> to its JSON text and back.</summary>
public sealed class LocalizedNameConverter : ValueConverter<LocalizedName, string>
{
    public LocalizedNameConverter()
        : base(name => name.ToJson(), json => LocalizedName.FromJson(json))
    {
    }
}

/// <summary>Value semantics for change tracking: two names with the same texts are the same value.</summary>
public sealed class LocalizedNameComparer : ValueComparer<LocalizedName>
{
    public LocalizedNameComparer()
        : base((a, b) => object.Equals(a, b), name => name.GetHashCode(), name => name)
    {
    }
}

public static class LocalizedNamePropertyBuilderExtensions
{
    /// <summary>
    /// Stores the property as a required <c>jsonb</c> column (AD-06) with the Platform converter and comparer.
    /// Pair it with <see cref="LocalizedNameDbContextOptionsBuilderExtensions.UseLocalizedNameTranslation"/>
    /// on the context so <see cref="LocalizedNameQuery"/> translates to SQL.
    /// </summary>
    public static PropertyBuilder<LocalizedName> HasLocalizedNameColumn(this PropertyBuilder<LocalizedName> builder)
    {
        return builder
            .HasConversion(new LocalizedNameConverter(), new LocalizedNameComparer())
            .HasColumnType("jsonb")
            .IsRequired();
    }
}
