using System;
using System.Collections.Generic;

namespace OpenTms.Platform.TestData;

/// <summary>
/// What one "Reset to dataset" run shares between the module contributors: the profile, one seeded random
/// generator (the same dataset comes out the same every time), the real addresses, and the typed outputs the
/// contributors publish for the ones after them.
/// </summary>
public sealed class TestDatasetContext
{
    private readonly Dictionary<Type, object> _outputs = new();

    public TestDatasetContext(Guid tenantId, TestDatasetProfile profile, TestAddressBook addresses, DateTime today)
    {
        TenantId = tenantId;
        Profile = profile;
        Addresses = addresses;
        Today = today.Date;
        Random = new Random(profile.Seed);
    }

    public Guid TenantId { get; }

    public TestDatasetProfile Profile { get; }

    public Random Random { get; }

    public TestAddressBook Addresses { get; }

    /// <summary>The run's "today"; order dates spread around it.</summary>
    public DateTime Today { get; }

    /// <summary>A random count between the profile's bounds for one master-data type.</summary>
    public int NextCount() => Profile.NextCount(Random);

    /// <summary>Makes an output (a type in the publishing module's Domain.Shared) available to later contributors.</summary>
    public void Publish<T>(T output) where T : class => _outputs[typeof(T)] = output;

    public T? Find<T>() where T : class => _outputs.TryGetValue(typeof(T), out var output) ? (T)output : null;

    public T Get<T>() where T : class =>
        Find<T>() ?? throw new InvalidOperationException(
            $"No {typeof(T).Name} was published; the contributor that owns it must run first (check Order).");
}
