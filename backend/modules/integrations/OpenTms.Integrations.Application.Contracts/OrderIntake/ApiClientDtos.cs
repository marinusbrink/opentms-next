using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using OpenTms.Platform.Grid;
using OpenTms.Platform.ReferenceData;
using Volo.Abp.Application.Services;

namespace OpenTms.Integrations.OrderIntake;

/// <summary>
/// Master data → Integrations → Order intake → API clients (design 72 "API client"), route root
/// <c>/api/integrations/api-client</c>. Each client is an OpenIddict client-credentials application in the host
/// database; its secret is returned once, by create and regenerate, and never again.
/// </summary>
public interface IApiClientAppService : IApplicationService
{
    Task<GridResponse<ApiClientRowDto>> GetListAsync(ApiClientListRequestDto input);

    Task<ApiClientDto> GetAsync(Guid id);

    Task<ApiClientSecretDto> CreateAsync(ApiClientCreateDto input);

    Task<ApiClientDto> UpdateAsync(Guid id, ApiClientUpdateDto input);

    Task<ApiClientSecretDto> RegenerateSecretAsync(Guid id);

    Task<ApiClientDto> ActivateAsync(Guid id);

    /// <summary>Removes the client's permission grant; tokens it still holds no longer pass the intake API.</summary>
    Task<ApiClientDto> DeactivateAsync(Guid id);

    /// <summary>Deletes the client and its OpenIddict application.</summary>
    Task DeleteAsync(Guid id);
}

/// <summary>The design-50 paging; <see cref="ReferenceDataListRequestDto.WildcardSearch"/> searches the name and the client id.</summary>
public class ApiClientListRequestDto : ReferenceDataListRequestDto
{
    public bool? IsActive { get; set; }
}

public class ApiClientRowDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public Guid? CustomerRelationId { get; set; }

    public string? CustomerName { get; set; }

    public bool IsActive { get; set; }

    public DateTime? LastUsedAt { get; set; }
}

public class ApiClientDto : ReferenceDataDetailDtoBase
{
    public string Name { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public Guid? CustomerRelationId { get; set; }

    public string? CustomerName { get; set; }

    public Guid? DefaultTransportOrderTypeId { get; set; }

    public string? DefaultTransportOrderTypeName { get; set; }

    public bool AutoConfirm { get; set; }

    public string SecretLastFour { get; set; } = string.Empty;

    public DateTime SecretCreatedAt { get; set; }

    public DateTime? LastUsedAt { get; set; }
}

public class ApiClientCreateDto
{
    [Required, MaxLength(OrderIntakeConsts.MaxApiClientNameLength)]
    public string Name { get; set; } = string.Empty;

    public Guid? CustomerRelationId { get; set; }

    public Guid? DefaultTransportOrderTypeId { get; set; }

    public bool AutoConfirm { get; set; }
}

public class ApiClientUpdateDto : IReferenceDataUpdateDto
{
    [Required, MaxLength(OrderIntakeConsts.MaxApiClientNameLength)]
    public string Name { get; set; } = string.Empty;

    public Guid? CustomerRelationId { get; set; }

    public Guid? DefaultTransportOrderTypeId { get; set; }

    public bool AutoConfirm { get; set; }

    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}

/// <summary>The one-time answer of create and regenerate: everything the customer's system needs to get a token.</summary>
public class ApiClientSecretDto
{
    public Guid Id { get; set; }

    public string ClientId { get; set; } = string.Empty;

    /// <summary>Shown once; never stored in readable form, never returned again.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary><c>{App:SelfUrl}/connect/token</c>.</summary>
    public string TokenUrl { get; set; } = string.Empty;

    /// <summary><see cref="OrderIntakeConsts.ApiScope"/>.</summary>
    public string Scope { get; set; } = string.Empty;
}
