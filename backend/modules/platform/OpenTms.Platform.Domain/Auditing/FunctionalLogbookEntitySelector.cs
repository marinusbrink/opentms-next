using System;
using System.Linq;
using Volo.Abp.Domain.Entities;

namespace OpenTms.Platform.Auditing;

/// <summary>
/// Decides which entity types the functional logbook records (ABP entity history).
/// ABP ships with no selector at all, so without this nothing is recorded; the PO decided
/// (2026-10-10) that the functional logbook is on by default. Every entity is recorded except
/// the infrastructure tables below, whose changes are machine traffic (tokens, sessions, jobs,
/// outbox records, blobs, per-user settings) rather than facts a key user looks up.
/// An entity marked <c>[DisableAuditing]</c> stays out regardless of this selector.
/// </summary>
public static class FunctionalLogbookEntitySelector
{
    public const string Name = "OpenTms.FunctionalLogbook";

    /// <summary>Namespaces (prefix match) whose entities are infrastructure, not business facts.</summary>
    public static readonly string[] ExcludedNamespaces =
    [
        "Volo.Abp.OpenIddict",                          // applications, scopes, authorizations, tokens
        "Volo.Abp.AuditLogging",                        // the logbooks themselves
        "Volo.Abp.BackgroundJobs",                      // job queue records
        "Volo.Abp.BlobStoring",                         // blob containers and content
        "Volo.Abp.SettingManagement",                   // settings incl. per-user grid layouts
        "Volo.Abp.EntityFrameworkCore.DistributedEvents" // transactional outbox / inbox records
    ];

    /// <summary>Single entity types that are infrastructure inside an otherwise recorded module.</summary>
    public static readonly string[] ExcludedTypeNames =
    [
        "Volo.Abp.Identity.IdentitySecurityLog",   // one row per sign-in attempt
        "Volo.Abp.Identity.IdentitySession",       // one row per session
        "Volo.Abp.Identity.IdentityUserToken",     // refresh/recovery tokens
        "Volo.Abp.Identity.IdentityUserPasswordHistory",
        "OpenTms.Platform.Maintenance.Instances.ApplicationInstance" // one heartbeat per instance per period
    ];

    public static bool IsSelected(Type entityType)
    {
        if (!typeof(IEntity).IsAssignableFrom(entityType))
            return false;

        var fullName = entityType.FullName ?? string.Empty;

        if (ExcludedTypeNames.Contains(fullName, StringComparer.Ordinal))
            return false;

        return !ExcludedNamespaces.Any(ns =>
            fullName.StartsWith(ns + ".", StringComparison.Ordinal));
    }
}
