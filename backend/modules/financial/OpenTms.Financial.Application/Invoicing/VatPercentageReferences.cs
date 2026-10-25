using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace OpenTms.Financial.Invoicing;

/// <summary>
/// The VAT category references of the Tariffs services (design 52): an additional service and a discount or surcharge
/// point at a VAT category of this module (restrict FK). Checks a reference exists in the tenant and names a page of
/// them in one query.
/// </summary>
public class VatPercentageReferences : ITransientDependency
{
    private readonly IRepository<VatPercentage, Guid> _vatPercentages;
    private readonly IAsyncQueryableExecuter _executer;

    public VatPercentageReferences(IRepository<VatPercentage, Guid> vatPercentages, IAsyncQueryableExecuter executer)
    {
        _vatPercentages = vatPercentages;
        _executer = executer;
    }

    public Task<IQueryable<VatPercentage>> GetQueryableAsync() => _vatPercentages.GetQueryableAsync();

    /// <summary>Null is allowed (the tenant's default VAT); any other id must be a visible category of this tenant.</summary>
    public async Task EnsureExistsAsync(Guid? id)
    {
        if (id != null && !await _vatPercentages.AnyAsync(v => v.Id == id.Value))
        {
            throw new BusinessException(ReferenceDataErrorCodes.ReferenceNotFound).WithData("field", "vatPercentageId");
        }
    }

    /// <summary>Id → name in the request culture, for the given ids (one IN query).</summary>
    public async Task<Dictionary<Guid, string>> GetNamesAsync(IEnumerable<Guid?> ids)
    {
        var keys = ids.Where(id => id != null).Select(id => id!.Value).Distinct().ToList();
        if (keys.Count == 0)
        {
            return [];
        }

        var query = (await _vatPercentages.GetQueryableAsync()).Where(v => keys.Contains(v.Id));
        var rows = await _executer.ToListAsync(query);
        return rows.ToDictionary(v => v.Id, v => v.Name.Resolve(CultureInfo.CurrentUICulture.Name));
    }
}
