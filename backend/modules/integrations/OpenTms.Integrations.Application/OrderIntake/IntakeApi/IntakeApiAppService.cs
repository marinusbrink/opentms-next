using System;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using OpenTms.Integrations.Features;
using OpenTms.Integrations.Permissions;
using Volo.Abp;
using Volo.Abp.Content;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Features;
using Volo.Abp.Security.Claims;
using Volo.Abp.Timing;
using Volo.Abp.Validation;

namespace OpenTms.Integrations.OrderIntake.IntakeApi;

/// <summary>
/// The intake API (design 72 "Intake API"), served by <c>IntakeApiController</c> under <c>/api/integrations/intake</c>; not
/// a conventional controller. The caller is an API client: its token carries the tenant (put there by the token server
/// from the client's application, never from <c>__tenant</c>) and <see cref="OrderIntakeConsts.ApiClientIdProperty"/>,
/// which must name an active client of the current tenant — else 403 (<see cref="IntegrationsErrorCodes.ApiClientInactive"/>).
/// The ExternalId is the caller's idempotency key: the same one again returns the stored result and creates nothing.
/// </summary>
[RemoteService(IsEnabled = false)]
[Authorize(IntegrationsPermissions.IntakeApi.Submit)]
[RequiresFeature(IntegrationsFeatures.OrderIntake)]
public class IntakeApiAppService : IntegrationsAppServiceBase, IIntakeApiAppService
{
    public const string JsonFileName = "order.json";

    private readonly IRepository<ApiClient, Guid> _clients;
    private readonly IRepository<InboundMessage, Guid> _messages;
    private readonly IInboundMessageRecorder _recorder;
    private readonly IInboundMessageProcessor _processor;
    private readonly IntakeResultBuilder _results;
    private readonly ICurrentPrincipalAccessor _principalAccessor;
    private readonly IClock _clock;

    public IntakeApiAppService(
        IRepository<ApiClient, Guid> clients,
        IRepository<InboundMessage, Guid> messages,
        IInboundMessageRecorder recorder,
        IInboundMessageProcessor processor,
        IntakeResultBuilder results,
        ICurrentPrincipalAccessor principalAccessor,
        IClock clock)
    {
        _clients = clients;
        _messages = messages;
        _recorder = recorder;
        _processor = processor;
        _results = results;
        _principalAccessor = principalAccessor;
        _clock = clock;
    }

    public async Task<IntakeResultDto> SubmitOrderAsync(IntakeOrderRequestDto input)
    {
        var client = await GetCallingClientAsync();
        var externalId = input.ExternalId.Trim();
        var existing = await FindMessageAsync(client, externalId);
        if (existing != null)
        {
            return await _results.BuildAsync(existing);
        }

        /* The body as the raw file of the one ApiJson document (design 72): the request in the intake's JSON shape. */
        var body = JsonSerializer.SerializeToUtf8Bytes(input, IntakeJson.Options);
        var recorded = await _recorder.RecordAsync(new InboundMessageRecording
        {
            Channel = InboundChannel.Api,
            SourceId = client.Id,
            ExternalId = externalId,
            ReceivedAt = _clock.Now,
            Documents =
            {
                new InboundDocumentRecording
                {
                    Kind = InboundDocumentKind.ApiJson,
                    FileName = JsonFileName,
                    ContentType = "application/json",
                    Content = body,
                    Format = DocumentFormat.Json
                }
            }
        });

        if (!recorded.IsNew)
        {
            return await _results.BuildAsync(await _messages.GetAsync(recorded.InboundMessageId));
        }

        Logger.LogInformation("Intake API: message {InboundMessageId} received from API client {ApiClientId}.", recorded.InboundMessageId, client.Id);
        return await _processor.ProcessCanonicalAsync(recorded.InboundMessageId, input.Order);
    }

    public async Task<IntakeResultDto> SubmitDocumentAsync(string externalId, IRemoteStreamContent file)
    {
        externalId = ValidateExternalId(externalId);
        if (file == null)
        {
            throw Invalid("A file is required.", "file");
        }

        var client = await GetCallingClientAsync();
        var existing = await FindMessageAsync(client, externalId);
        if (existing != null)
        {
            return await _results.BuildAsync(existing);
        }

        if (file.ContentLength > OrderIntakeConsts.MaxDocumentBytes)
        {
            throw TooLarge();
        }

        var format = FormatOf(file.FileName, file.ContentType);
        var content = await ReadAsync(file);
        var recorded = await _recorder.RecordAsync(new InboundMessageRecording
        {
            Channel = InboundChannel.Api,
            SourceId = client.Id,
            ExternalId = externalId,
            ReceivedAt = _clock.Now,
            Documents =
            {
                new InboundDocumentRecording
                {
                    Kind = InboundDocumentKind.ApiFile,
                    FileName = SafeFileName(file.FileName),
                    ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType.Trim(),
                    Content = content,
                    Format = format
                }
            }
        });

        var message = await _messages.GetAsync(recorded.InboundMessageId);
        if (!recorded.IsNew)
        {
            return await _results.BuildAsync(message);
        }

        Logger.LogInformation("Intake API: document message {InboundMessageId} received from API client {ApiClientId}.", message.Id, client.Id);
        return new IntakeResultDto { InboundMessageId = message.Id, Status = message.Status };
    }

    public async Task<IntakeResultDto?> GetByExternalIdAsync(string externalId)
    {
        externalId = ValidateExternalId(externalId);
        var client = await GetCallingClientAsync();
        var message = await FindMessageAsync(client, externalId);
        return message == null ? null : await _results.BuildAsync(message);
    }

    /// <summary>
    /// The active API client of the current tenant the token names; records its use. The token's client_id must be the
    /// client's own (an application of another client cannot borrow the id).
    /// </summary>
    private async Task<ApiClient> GetCallingClientAsync()
    {
        var principal = _principalAccessor.Principal;
        if (!CurrentTenant.IsAvailable || !Guid.TryParse(principal?.FindFirst(OrderIntakeConsts.ApiClientIdProperty)?.Value, out var apiClientId))
        {
            throw new BusinessException(IntegrationsErrorCodes.ApiClientInactive);
        }

        var client = await _clients.FindAsync(apiClientId);
        var tokenClientId = principal!.FindFirst(AbpClaimTypes.ClientId)?.Value;
        if (client is not { IsActive: true } || (tokenClientId != null && tokenClientId != client.ClientId))
        {
            Logger.LogInformation("Intake API: refused a call of API client {ApiClientId} in tenant {TenantId} (unknown or inactive).", apiClientId, CurrentTenant.Id);
            throw new BusinessException(IntegrationsErrorCodes.ApiClientInactive);
        }

        client.RecordUse(_clock.Now);
        await _clients.UpdateAsync(client, autoSave: true);
        return client;
    }

    private Task<InboundMessage?> FindMessageAsync(ApiClient client, string externalId)
        => _messages.FirstOrDefaultAsync(m => m.Channel == InboundChannel.Api && m.SourceId == client.Id && m.ExternalId == externalId);

    private static string ValidateExternalId(string? externalId)
    {
        var trimmed = externalId?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > OrderIntakeConsts.MaxApiExternalIdLength)
        {
            throw Invalid($"externalId is required and at most {OrderIntakeConsts.MaxApiExternalIdLength} characters.", "externalId");
        }

        return trimmed;
    }

    private static async Task<byte[]> ReadAsync(IRemoteStreamContent file)
    {
        await using var stream = file.GetStream();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk)) > 0)
        {
            if (buffer.Length + read > OrderIntakeConsts.MaxDocumentBytes)
            {
                throw TooLarge();
            }

            buffer.Write(chunk, 0, read);
        }

        if (buffer.Length == 0)
        {
            throw Invalid("The file is empty.", "file");
        }

        return buffer.ToArray();
    }

    /// <summary>The format by extension, else by content type; anything else is refused (400).</summary>
    public static DocumentFormat FormatOf(string? fileName, string? contentType)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        var byExtension = extension switch
        {
            ".pdf" => DocumentFormat.Pdf,
            ".xlsx" => DocumentFormat.Xlsx,
            ".csv" => DocumentFormat.Csv,
            ".txt" => DocumentFormat.Text,
            ".xml" => DocumentFormat.Xml,
            ".json" => DocumentFormat.Json,
            ".htm" or ".html" => DocumentFormat.Html,
            _ => DocumentFormat.Unknown
        };
        if (byExtension != DocumentFormat.Unknown)
        {
            return byExtension;
        }

        var type = (contentType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();
        return type switch
        {
            "application/pdf" => DocumentFormat.Pdf,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => DocumentFormat.Xlsx,
            "text/csv" => DocumentFormat.Csv,
            "text/plain" => DocumentFormat.Text,
            "application/xml" or "text/xml" => DocumentFormat.Xml,
            "application/json" => DocumentFormat.Json,
            "text/html" => DocumentFormat.Html,
            _ => throw new BusinessException(IntegrationsErrorCodes.UnsupportedFormat).WithData("contentType", type.Length == 0 ? "?" : type)
        };
    }

    private static string? SafeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var name = Path.GetFileName(fileName.Replace('\\', '/')).Trim();
        return name.Length <= OrderIntakeConsts.MaxFileNameLength ? name : name[^OrderIntakeConsts.MaxFileNameLength..];
    }

    private static BusinessException TooLarge()
        => new BusinessException(IntegrationsErrorCodes.DocumentTooLarge).WithData("maxBytes", OrderIntakeConsts.MaxDocumentBytes);

    private static AbpValidationException Invalid(string message, string member)
        => new([new ValidationResult(message, [member])]);
}
