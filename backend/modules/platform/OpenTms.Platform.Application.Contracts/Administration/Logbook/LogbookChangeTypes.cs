using System;
using System.Linq;

namespace OpenTms.Platform.Administration.Logbook;

/// <summary>
/// The change kinds of the functional logbook, as the API exposes them (ABP's <c>EntityChangeType</c>
/// by name, so the contract carries words instead of enum ordinals).
/// </summary>
public static class LogbookChangeTypes
{
    public const string Created = "Created";
    public const string Updated = "Updated";
    public const string Deleted = "Deleted";

    public static readonly string[] All = [Created, Updated, Deleted];

    public static bool IsValid(string? value) =>
        value != null && All.Any(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase));
}
