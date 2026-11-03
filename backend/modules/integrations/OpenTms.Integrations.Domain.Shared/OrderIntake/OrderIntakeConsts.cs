using System;

namespace OpenTms.Integrations.OrderIntake;

/// <summary>Column lengths, limits and fixed names of the order intake (design 72).</summary>
public static class OrderIntakeConsts
{
    /// <summary>
    /// An API client's and an import profile's <c>LastUsedAt</c> are written at most this often (design 72 build note 19).
    /// </summary>
    public static readonly TimeSpan UsageStampInterval = TimeSpan.FromMinutes(5);

    /* Mailbox connections. */

    public const int MaxDisplayNameLength = 128;

    /// <summary>An e-mail address (RFC 5321).</summary>
    public const int MaxEmailAddressLength = 320;

    public const int MaxFolderIdLength = 256;

    public const int MaxFolderNameLength = 256;

    /// <summary>The refresh token as <c>IStringEncryptionService</c> stores it.</summary>
    public const int MaxRefreshTokenLength = 4096;

    /// <summary>Gmail's historyId or Graph's delta link.</summary>
    public const int MaxSyncCursorLength = 2048;

    public const int MaxErrorCodeLength = 128;

    /// <summary>The data-protection purpose of the OAuth <c>state</c>.</summary>
    public const string OAuthStatePurpose = "OpenTms.Integrations.MailboxOAuth";

    public static readonly TimeSpan OAuthStateLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Microsoft lists folders recursively to this depth ("Inbox/Orders/Customer").</summary>
    public const int MaxFolderDepth = 3;

    /* API clients. */

    public const int MaxApiClientNameLength = 128;

    public const int MaxClientIdLength = 64;

    /// <summary>A client id is this prefix plus <see cref="ClientIdRandomLength"/> random base32 characters.</summary>
    public const string ClientIdPrefix = "otms_";

    public const int ClientIdRandomLength = 24;

    /// <summary>The random characters of a secret; shown once.</summary>
    public const int ClientSecretLength = 40;

    public const int SecretLastFourLength = 4;

    /// <summary>The OpenIddict application property and access-token claim that carry the tenant of an API client.</summary>
    public const string TenantIdProperty = "opentms_tenant_id";

    /// <summary>The OpenIddict application property and access-token claim that carry the API client's id.</summary>
    public const string ApiClientIdProperty = "opentms_api_client_id";

    /// <summary>The scope a client-credentials token asks for.</summary>
    public const string ApiScope = "OpenTms";

    /* Inbound messages and documents. */

    public const int MaxExternalIdLength = 512;

    /// <summary>The intake API's idempotency key (<c>IntakeOrderRequestDto.ExternalId</c>).</summary>
    public const int MaxApiExternalIdLength = 200;

    public const int MaxFromLength = 320;

    public const int MaxSubjectLength = 512;

    public const int MaxBlobNameLength = 256;

    public const int MaxFileNameLength = 256;

    public const int MaxContentTypeLength = 128;

    /// <summary>Hex SHA-256.</summary>
    public const int Sha256Length = 64;

    /// <summary>The lowercased From address, or <c>api:{clientId}</c>.</summary>
    public const int MaxSenderKeyLength = 320;

    /// <summary>Hex SHA-256 of the layout's labels, headers or element paths.</summary>
    public const int FingerprintLength = 64;

    /// <summary>The text of the source a value was read from, as the trace shows it.</summary>
    public const int MaxSnippetLength = 120;

    /// <summary>The reference of a source location: a cell, "line 14", an element path.</summary>
    public const int MaxSourceRefLength = 512;

    /// <summary>The localization key of a step's text.</summary>
    public const int MaxMessageKeyLength = 128;

    public const int MaxStepParameters = 20;

    public const int MaxStepParameterKeyLength = 64;

    public const int MaxStepParameterValueLength = 512;

    public const int MaxModelIdLength = 64;

    /// <summary>Orders' order number.</summary>
    public const int MaxOrderNumberLength = 32;

    /// <summary>The blob container of raw messages and documents.</summary>
    public const string BlobContainerName = "opentms-inbound";

    /// <summary>The blob name of a message's raw RFC 822 content: <c>{messageId}/message.eml</c>.</summary>
    public const string RawMessageBlobFileName = "message.eml";

    public const long MaxDocumentBytes = 25L * 1024 * 1024;

    public const long MaxMessageBytes = 40L * 1024 * 1024;

    public const long MaxXmlBytes = 20L * 1024 * 1024;

    /// <summary>A mail body is a document when it has more non-whitespace characters than this, quotes and signature removed.</summary>
    public const int MinMailBodyCharacters = 40;

    /* Import profiles. */

    public const int MaxProfileNameLength = 128;

    /// <summary>The target type of a profile.</summary>
    public const int MaxTargetLength = 64;

    /// <summary>The only target of this design: the transport order.</summary>
    public const string TransportOrderTarget = "Orders.TransportOrder";

    /// <summary>The headers, element paths or labels a profile's signature may hold.</summary>
    public const int MaxSignatureEntries = 100;

    public const int MaxSignatureEntryLength = 256;

    /// <summary>The setting <c>Integrations.OrderIntake.ProfileActivationThreshold</c>: default and bounds.</summary>
    public const int DefaultProfileActivationThreshold = 3;

    public const int MinProfileActivationThreshold = 1;

    public const int MaxProfileActivationThreshold = 20;

    /* AI. */

    /// <summary>An AI extraction without a verified profile is never more confident than this.</summary>
    public const decimal UnverifiedConfidenceCap = 0.6m;

    public const string DefaultLearningModel = "claude-opus-5-5";

    public const string DefaultExtractionModel = "claude-opus-5-5";

    public const int MaxAgentLength = 64;

    /// <summary>The <c>Agent</c> of an AI usage record (AP-05): learning a layout, extracting without a profile, repairing a profile.</summary>
    public const string LearnAgent = "Integrations.OrderIntake.Learn";

    public const string ExtractAgent = "Integrations.OrderIntake.Extract";

    public const string RepairAgent = "Integrations.OrderIntake.Repair";
}
