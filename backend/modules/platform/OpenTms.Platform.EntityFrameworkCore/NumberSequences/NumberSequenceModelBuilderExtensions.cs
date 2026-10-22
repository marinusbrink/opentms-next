using System;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace OpenTms.Platform.NumberSequences;

public static class NumberSequenceModelBuilderExtensions
{
    /// <summary>
    /// Maps a numbered type's <see cref="NumberSequenceFormat"/> as required owned columns <c>Numbering*</c> on the
    /// type's own table (design 50), so every module stores the format the same way.
    /// </summary>
    public static EntityTypeBuilder<TEntity> HasNumbering<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, NumberSequenceFormat?>> navigation)
        where TEntity : class
    {
        builder.OwnsOne(navigation, n =>
        {
            n.Property(f => f.Prefix).HasColumnName("NumberingPrefix").HasMaxLength(NumberSequenceFormat.MaxPrefixLength);
            n.Property(f => f.IncludeYear).HasColumnName("NumberingIncludeYear");
            n.Property(f => f.IncludeMonth).HasColumnName("NumberingIncludeMonth");
            n.Property(f => f.Separator).HasColumnName("NumberingSeparator").HasMaxLength(1).IsRequired();
            n.Property(f => f.Digits).HasColumnName("NumberingDigits");
        });
        builder.Navigation(navigation!).IsRequired();
        return builder;
    }
}
