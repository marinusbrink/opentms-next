using System;

namespace OpenTms.Reporting.Facts;

/// <summary>Functions that exist only in SQL; <c>ReportingDbContext</c> maps them.</summary>
public static class ReportingDbFunctions
{
    /// <summary>In a grouped query: the number of groups before <c>Take</c> limits them.</summary>
    public static long GroupsBeforeLimit() => throw new NotSupportedException("Only translates to SQL.");
}
