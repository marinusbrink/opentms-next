using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.MasterData.Drivers;
using OpenTms.MasterData.Relations;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace OpenTms.MasterData.PersonalData;

/// <summary>GDPR art. 15: the contact persons and drivers of the current tenant that match an e-mail address or user id.</summary>
public class PersonalDataSource : IPersonalDataSource, ITransientDependency
{
    private readonly IRepository<RelationContactPerson, Guid> _contactPersons;
    private readonly IRepository<Driver, Guid> _drivers;

    public PersonalDataSource(IRepository<RelationContactPerson, Guid> contactPersons, IRepository<Driver, Guid> drivers)
    {
        _contactPersons = contactPersons;
        _drivers = drivers;
    }

    public async Task<List<PersonalDataRecordDto>> FindAsync(string? email, Guid? userId)
    {
        var mail = email?.Trim().ToLowerInvariant();
        var records = new List<PersonalDataRecordDto>();

        if (!string.IsNullOrEmpty(mail))
        {
            foreach (var c in await _contactPersons.GetListAsync(c => c.Email != null && c.Email.ToLower() == mail))
            {
                records.Add(new PersonalDataRecordDto
                {
                    Kind = "RelationContactPerson",
                    Id = c.Id,
                    RelationId = c.RelationId,
                    Fields = new Dictionary<string, string?>
                    {
                        ["firstName"] = c.FirstName, ["middleName"] = c.MiddleName, ["lastName"] = c.LastName, ["role"] = c.Role.ToString(),
                        ["jobTitle"] = c.JobTitle, ["department"] = c.Department, ["email"] = c.Email, ["phone"] = c.Phone, ["mobile"] = c.Mobile
                    }
                });
            }
        }

        var drivers = await _drivers.GetListAsync(d => (userId != null && d.UserId == userId) || (mail != null && d.Email != null && d.Email.ToLower() == mail), includeDetails: true);
        foreach (var d in drivers)
        {
            var fields = new Dictionary<string, string?>
            {
                ["code"] = d.Code, ["firstName"] = d.FirstName, ["middleName"] = d.MiddleName, ["lastName"] = d.LastName, ["initials"] = d.Initials,
                ["email"] = d.Email, ["phone"] = d.Phone, ["mobile"] = d.Mobile, ["preferredCulture"] = d.PreferredCulture,
                ["employerRelationId"] = d.EmployerRelationId?.ToString(), ["userId"] = d.UserId?.ToString()
            };
            foreach (var q in d.Qualifications.Select((q, i) => (q, i)))
            {
                fields[$"qualification{q.i + 1}"] = $"{q.q.Kind} {q.q.Number} {q.q.ValidFrom:yyyy-MM-dd}..{q.q.ValidUntil:yyyy-MM-dd}".Trim();
            }

            records.Add(new PersonalDataRecordDto { Kind = "Driver", Id = d.Id, RelationId = d.EmployerRelationId, Fields = fields });
        }

        return records;
    }
}
