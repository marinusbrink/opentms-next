using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace OpenTms.Integrations.OrderIntake;

/// <summary>
/// A customer system's access to the intake API (design 72 "API client"): the tenant side of an OpenIddict
/// client-credentials application in the host database, which carries this row's tenant and id as properties. The
/// secret itself is never stored here: only its last four characters and when it was made. Deactivating removes the
/// client's permission grant; <see cref="ClientId"/> is unique across all tenants.
/// </summary>
public class ApiClient : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public Guid? TenantId { get; private set; }

    public string Name { get; private set; } = default!;

    /// <summary><c>otms_</c> + 24 random base32 characters.</summary>
    public string ClientId { get; private set; } = default!;

    public bool IsActive { get; private set; }

    /// <summary>Orders from this client are this customer's, whatever the payload says.</summary>
    public Guid? CustomerRelationId { get; private set; }

    public Guid? DefaultTransportOrderTypeId { get; private set; }

    public bool AutoConfirm { get; private set; }

    public string SecretLastFour { get; private set; } = default!;

    public DateTime SecretCreatedAt { get; private set; }

    public DateTime? LastUsedAt { get; private set; }

    protected ApiClient()
    {
    }

    public ApiClient(
        Guid id, Guid? tenantId, string name, string clientId, string secretLastFour, DateTime secretCreatedAt,
        Guid? customerRelationId, Guid? defaultTransportOrderTypeId, bool autoConfirm)
        : base(id)
    {
        TenantId = tenantId;
        Check.NotNullOrWhiteSpace(clientId, nameof(clientId), OrderIntakeConsts.MaxClientIdLength);
        if (!clientId.StartsWith(OrderIntakeConsts.ClientIdPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException($"A client id starts with '{OrderIntakeConsts.ClientIdPrefix}'.", nameof(clientId));
        }

        ClientId = clientId;
        IsActive = true;
        Update(name, customerRelationId, defaultTransportOrderTypeId, autoConfirm);
        RecordSecret(secretLastFour, secretCreatedAt);
    }

    public void Update(string name, Guid? customerRelationId, Guid? defaultTransportOrderTypeId, bool autoConfirm)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), OrderIntakeConsts.MaxApiClientNameLength).Trim();
        CustomerRelationId = customerRelationId;
        DefaultTransportOrderTypeId = defaultTransportOrderTypeId;
        AutoConfirm = autoConfirm;
    }

    /// <summary>A new secret was set on the OpenIddict application (create or regenerate).</summary>
    public void RecordSecret(string secretLastFour, DateTime createdAt)
    {
        Check.NotNullOrWhiteSpace(secretLastFour, nameof(secretLastFour));
        if (secretLastFour.Length != OrderIntakeConsts.SecretLastFourLength)
        {
            throw new ArgumentException($"The last {OrderIntakeConsts.SecretLastFourLength} characters of the secret.", nameof(secretLastFour));
        }

        SecretLastFour = secretLastFour;
        SecretCreatedAt = createdAt;
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    public void RecordUse(DateTime at) => LastUsedAt = at;
}
