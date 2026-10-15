using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenTms.Platform.DataProtection;

/// <summary>Where every instance keeps its ASP.NET Core data-protection keys: the host database (<see cref="HostDataProtectionKeyStore"/>).</summary>
public interface IDataProtectionKeyStore
{
    /// <summary>Every stored key element as XML text, oldest first.</summary>
    Task<IReadOnlyList<string>> GetAllAsync();

    Task AddAsync(string? friendlyName, string xml);
}
