using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTms.MasterData.PersonalData;

/// <summary>
/// GDPR art. 15 (design 43, security quickscan): the module's part of a personal-data export. The Platform
/// export job (a later design) calls every module's source with an e-mail address or user id and assembles
/// the file; this returns the matching contact persons and drivers of the current tenant as records.
/// </summary>
public interface IPersonalDataSource
{
    Task<List<PersonalDataRecordDto>> FindAsync(string? email, Guid? userId);
}

public class PersonalDataRecordDto
{
    /// <summary>"Driver" or "RelationContactPerson".</summary>
    public string Kind { get; set; } = string.Empty;
    public Guid Id { get; set; }
    public Guid? RelationId { get; set; }
    public Dictionary<string, string?> Fields { get; set; } = new();
}
