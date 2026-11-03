using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenTms.Integrations.OrderIntake.Extraction;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;

namespace OpenTms.Integrations.OrderIntake.Profiles;

/// <summary>The outcome of matching: the profile used with its run, and the profiles whose signature fit but whose run failed validation (layout drift).</summary>
public sealed class ProfileMatch
{
    public ImportProfile? Profile { get; init; }

    public ProfileRunResult? Run { get; init; }

    public List<(ImportProfile Profile, ProfileRunResult Run)> Failed { get; init; } = new();
}

/// <summary>
/// Finds the import profile of a document (design 72 §4 "Matching"): candidates are the tenant's profiles that are not
/// Retired, of the document's format, whose sender key is the sender's address, <c>@</c> + its domain, or null; most
/// specific first, then Active before Proposed, then the most confirmed. The first whose whole signature is present in
/// the document and whose run passes validation is used. Tenant-filtered like every repository read.
/// </summary>
public class ImportProfileMatcher : ITransientDependency
{
    /// <summary>A tenant has a handful of layouts per sender; the candidate read is bounded all the same.</summary>
    public const int MaxCandidates = 100;

    private readonly IRepository<ImportProfile, Guid> _profiles;
    private readonly IAsyncQueryableExecuter _executer;
    private readonly ImportProfileExecutor _executor;

    public ImportProfileMatcher(IRepository<ImportProfile, Guid> profiles, IAsyncQueryableExecuter executer, ImportProfileExecutor executor)
    {
        _profiles = profiles;
        _executer = executer;
        _executor = executor;
    }

    /// <summary>The sender keys a profile may carry for this sender: the address, its domain, and (always) null.</summary>
    public static List<string> SenderKeys(string? senderKey)
    {
        var keys = new List<string>();
        if (string.IsNullOrWhiteSpace(senderKey))
        {
            return keys;
        }

        keys.Add(senderKey);
        var at = senderKey.LastIndexOf('@');
        if (at > 0 && at < senderKey.Length - 1 && !senderKey.StartsWith("api:", StringComparison.Ordinal))
        {
            keys.Add(senderKey[at..]);
        }

        return keys;
    }

    public async Task<List<ImportProfile>> GetCandidatesAsync(DocumentFormat format, string? senderKey, CancellationToken cancellationToken = default)
    {
        var keys = SenderKeys(senderKey);
        var query = (await _profiles.GetQueryableAsync())
            .Where(p => p.Format == format && p.Status != ImportProfileStatus.Retired && (p.SenderKey == null || keys.Contains(p.SenderKey!)))
            .OrderBy(p => p.Id)
            .Take(MaxCandidates);
        var candidates = await _executer.ToListAsync(query, cancellationToken);
        return Order(candidates, keys);
    }

    public static List<ImportProfile> Order(IEnumerable<ImportProfile> candidates, IReadOnlyList<string> keys)
        => candidates
            .OrderBy(p => p.SenderKey == null ? int.MaxValue : Math.Max(0, IndexOf(keys, p.SenderKey)))
            .ThenBy(p => p.Status == ImportProfileStatus.Active ? 0 : 1)
            .ThenByDescending(p => p.ConfirmedCount)
            .ThenBy(p => p.CreationTime)
            .ThenBy(p => p.Id)
            .ToList();

    /// <summary>Runs the candidates in order on the document; pure apart from the candidates given.</summary>
    public ProfileMatch Match(IReadOnlyList<ImportProfile> candidates, ExtractedDocument document)
    {
        var failed = new List<(ImportProfile, ProfileRunResult)>();
        foreach (var profile in candidates)
        {
            if (!profile.Signature.All(document.Contains))
            {
                continue;
            }

            var run = _executor.Run(profile.Rules, document);
            if (run.IsValid)
            {
                return new ProfileMatch { Profile = profile, Run = run, Failed = failed };
            }

            failed.Add((profile, run));
        }

        return new ProfileMatch { Failed = failed };
    }

    private static int IndexOf(IReadOnlyList<string> keys, string key)
    {
        for (var i = 0; i < keys.Count; i++)
        {
            if (string.Equals(keys[i], key, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return keys.Count;
    }
}
