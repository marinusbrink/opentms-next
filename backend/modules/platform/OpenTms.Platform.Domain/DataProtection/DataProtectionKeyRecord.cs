using System;
using Volo.Abp.Domain.Entities;

namespace OpenTms.Platform.DataProtection;

/// <summary>
/// One ASP.NET Core data-protection key as its key manager writes it: an XML element with a friendly name
/// (docs/designs/tenants-and-editions.md, "Shared data-protection keys"). The keys protect the auth cookie and the
/// "Log in as" tickets, so every instance must read the same ones. Host-owned: written and read in the host database
/// only (the table exists in every database because the schema is shared; tenant databases keep it empty). Rows are
/// only ever added; the key manager revokes a key by adding a revocation element, never by changing one.
/// </summary>
public class DataProtectionKeyRecord : BasicAggregateRoot<Guid>
{
    public const int MaxFriendlyNameLength = 256;

    public string? FriendlyName { get; private set; }

    public string Xml { get; private set; } = string.Empty;

    public DateTime CreatedAt { get; private set; }

    protected DataProtectionKeyRecord()
    {
    }

    public DataProtectionKeyRecord(Guid id, string? friendlyName, string xml, DateTime createdAt)
        : base(id)
    {
        FriendlyName = friendlyName;
        Xml = xml;
        CreatedAt = createdAt;
    }
}
