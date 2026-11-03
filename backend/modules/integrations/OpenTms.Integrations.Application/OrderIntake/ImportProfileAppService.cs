using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using OpenTms.Integrations.Features;
using OpenTms.Integrations.OrderIntake.Profiles;
using OpenTms.Integrations.Permissions;
using OpenTms.Platform.Features;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;

namespace OpenTms.Integrations.OrderIntake;

/// <summary>
/// Integrations app → Order intake → Import profiles (design 72 §4): the list (design-50 paging, filters status
/// and format, the wildcard search covers name and sender), one profile with its rules (read-only: rules change through
/// the review's repair), rename, activate, retire and delete. Feature <c>Integrations.OrderIntake</c> plus the app
/// features of the node; tenant-filtered.
/// </summary>
[RequiresFeature(
    IntegrationsFeatures.OrderIntake, AppFeatures.Integrations, AppFeatures.IntegrationsApp.Intake.Default,
    AppFeatures.IntegrationsApp.Intake.ImportProfiles, RequiresAll = true)]
[Authorize(IntegrationsPermissions.ImportProfiles.Default)]
public class ImportProfileAppService : IntegrationsAppServiceBase, IImportProfileAppService
{
    private readonly IRepository<ImportProfile, Guid> _profiles;
    private readonly IRepository<InboundDocument, Guid> _documents;

    public ImportProfileAppService(IRepository<ImportProfile, Guid> profiles, IRepository<InboundDocument, Guid> documents)
    {
        _profiles = profiles;
        _documents = documents;
    }

    public virtual async Task<GridResponse<ImportProfileRowDto>> GetListAsync(ImportProfileListRequestDto input)
    {
        var all = await _profiles.GetQueryableAsync();
        var totalCount = await AsyncExecuter.LongCountAsync(all);

        var filtered = all;
        if (input.Status.Count > 0)
        {
            filtered = filtered.Where(p => input.Status.Contains(p.Status));
        }

        if (input.Format != null)
        {
            filtered = filtered.Where(p => p.Format == input.Format);
        }

        var term = input.WildcardSearch?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(term))
        {
            filtered = filtered.Where(p => p.Name.ToLower().Contains(term) || (p.SenderKey != null && p.SenderKey.Contains(term)));
        }

        var filteredCount = filtered == all ? totalCount : await AsyncExecuter.LongCountAsync(filtered);
        var descending = input.Descending;
        var ordered = (input.SortBy?.ToLowerInvariant()) switch
        {
            null or "" or "name" => descending ? filtered.OrderByDescending(p => p.Name).ThenByDescending(p => p.Id) : filtered.OrderBy(p => p.Name).ThenBy(p => p.Id),
            "senderkey" => descending ? filtered.OrderByDescending(p => p.SenderKey).ThenBy(p => p.Name).ThenBy(p => p.Id) : filtered.OrderBy(p => p.SenderKey).ThenBy(p => p.Name).ThenBy(p => p.Id),
            "format" => descending ? filtered.OrderByDescending(p => p.Format).ThenBy(p => p.Name).ThenBy(p => p.Id) : filtered.OrderBy(p => p.Format).ThenBy(p => p.Name).ThenBy(p => p.Id),
            "status" => descending ? filtered.OrderByDescending(p => p.Status).ThenBy(p => p.Name).ThenBy(p => p.Id) : filtered.OrderBy(p => p.Status).ThenBy(p => p.Name).ThenBy(p => p.Id),
            "version" => descending ? filtered.OrderByDescending(p => p.Version).ThenBy(p => p.Name).ThenBy(p => p.Id) : filtered.OrderBy(p => p.Version).ThenBy(p => p.Name).ThenBy(p => p.Id),
            "confirmedcount" => descending ? filtered.OrderByDescending(p => p.ConfirmedCount).ThenBy(p => p.Name).ThenBy(p => p.Id) : filtered.OrderBy(p => p.ConfirmedCount).ThenBy(p => p.Name).ThenBy(p => p.Id),
            "correctedcount" => descending ? filtered.OrderByDescending(p => p.CorrectedCount).ThenBy(p => p.Name).ThenBy(p => p.Id) : filtered.OrderBy(p => p.CorrectedCount).ThenBy(p => p.Name).ThenBy(p => p.Id),
            "lastusedat" => descending ? filtered.OrderByDescending(p => p.LastUsedAt).ThenBy(p => p.Name).ThenBy(p => p.Id) : filtered.OrderBy(p => p.LastUsedAt).ThenBy(p => p.Name).ThenBy(p => p.Id),
            _ => throw new BusinessException(ReferenceDataErrorCodes.UnknownSortColumn).WithData("sortBy", input.SortBy!)
        };

        var page = await AsyncExecuter.ToListAsync(ordered.Skip(input.StartRow).Take(input.EndRow - input.StartRow));
        return new GridResponse<ImportProfileRowDto>
        {
            Rows = page.Select(p => new ImportProfileRowDto
            {
                Id = p.Id,
                Name = p.Name,
                SenderKey = p.SenderKey,
                Format = p.Format,
                Status = p.Status,
                Version = p.Version,
                ConfirmedCount = p.ConfirmedCount,
                CorrectedCount = p.CorrectedCount,
                LastUsedAt = p.LastUsedAt
            }).ToList(),
            TotalCount = totalCount,
            FilteredCount = filteredCount
        };
    }

    public virtual async Task<ImportProfileDto> GetAsync(Guid id) => await ToDtoAsync(await _profiles.GetAsync(id));

    [Authorize(IntegrationsPermissions.ImportProfiles.Manage)]
    public virtual async Task<ImportProfileDto> RenameAsync(Guid id, ImportProfileRenameDto input)
    {
        var profile = await _profiles.GetAsync(id);
        profile.ConcurrencyStamp = input.ConcurrencyStamp;
        profile.Rename(input.Name);
        await _profiles.UpdateAsync(profile, autoSave: true);
        return await ToDtoAsync(profile);
    }

    [Authorize(IntegrationsPermissions.ImportProfiles.Manage)]
    public virtual async Task<ImportProfileDto> ActivateAsync(Guid id)
    {
        var profile = await _profiles.GetAsync(id);
        profile.Activate();
        await _profiles.UpdateAsync(profile, autoSave: true);
        return await ToDtoAsync(profile);
    }

    [Authorize(IntegrationsPermissions.ImportProfiles.Manage)]
    public virtual async Task<ImportProfileDto> RetireAsync(Guid id)
    {
        var profile = await _profiles.GetAsync(id);
        profile.Retire();
        await _profiles.UpdateAsync(profile, autoSave: true);
        return await ToDtoAsync(profile);
    }

    [Authorize(IntegrationsPermissions.ImportProfiles.Manage)]
    public virtual async Task DeleteAsync(Guid id)
    {
        var profile = await _profiles.GetAsync(id);
        await _profiles.DeleteAsync(profile, autoSave: true);
    }

    private async Task<ImportProfileDto> ToDtoAsync(ImportProfile profile)
    {
        var source = await _documents.FindAsync(profile.SourceDocumentId, includeDetails: false);
        return new ImportProfileDto
        {
            Id = profile.Id,
            CreationTime = profile.CreationTime,
            CreatorId = profile.CreatorId,
            LastModificationTime = profile.LastModificationTime,
            LastModifierId = profile.LastModifierId,
            ConcurrencyStamp = profile.ConcurrencyStamp,
            Name = profile.Name,
            SenderKey = profile.SenderKey,
            Format = profile.Format,
            Target = profile.Target,
            Status = profile.Status,
            Version = profile.Version,
            Origin = profile.Origin,
            ModelId = profile.ModelId,
            Rules = profile.Rules,
            PreviousRules = profile.PreviousRules,
            Signature = profile.Signature.ToList(),
            ConfirmedCount = profile.ConfirmedCount,
            CorrectedCount = profile.CorrectedCount,
            LastUsedAt = profile.LastUsedAt,
            SourceDocumentId = profile.SourceDocumentId,
            SourceInboundMessageId = source?.InboundMessageId
        };
    }
}
