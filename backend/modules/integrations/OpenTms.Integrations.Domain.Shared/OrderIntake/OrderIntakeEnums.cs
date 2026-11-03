namespace OpenTms.Integrations.OrderIntake;

/*
 * The enumerations of the order intake (design 72 "The pipeline"). Stored as integers; the values are part of the
 * contract and never renumbered.
 */

/// <summary>The mail provider of a mailbox connection.</summary>
public enum MailProvider
{
    Gmail = 1,
    Microsoft365 = 2
}

/// <summary>
/// A mailbox connection's lifecycle: created and waiting for the provider's consent, consented and waiting for a folder,
/// synchronising, paused by a user, or waiting for a new consent after the provider refused its refresh token.
/// </summary>
public enum MailboxStatus
{
    AwaitingConsent = 1,
    AwaitingFolder = 2,
    Active = 3,
    Paused = 4,
    Reconnect = 5,

    /// <summary>A folder was chosen; <c>MailboxActivationJob</c> takes the provider's cursor, then the connection is Active.</summary>
    Activating = 6
}

/// <summary>Where an inbound message came from.</summary>
public enum InboundChannel
{
    Gmail = 1,
    Microsoft365 = 2,
    Api = 3
}

/// <summary>The roll-up of an inbound message's documents.</summary>
public enum InboundMessageStatus
{
    Received = 1,
    Processing = 2,
    Completed = 3,
    NeedsAttention = 4,
    Failed = 5,
    NotAnOrder = 6
}

/// <summary>What an inbound document is within its message.</summary>
public enum InboundDocumentKind
{
    MailBody = 1,
    Attachment = 2,
    ApiJson = 3,
    ApiFile = 4
}

/// <summary>The format an extractor reads; ScannedPdf is a PDF without a usable text layer.</summary>
public enum DocumentFormat
{
    Unknown = 0,
    Text = 1,
    Html = 2,
    Pdf = 3,
    ScannedPdf = 4,
    Xlsx = 5,
    Csv = 6,
    Xml = 7,
    Json = 8
}

/// <summary>Where an inbound document is in the pipeline.</summary>
public enum InboundDocumentStatus
{
    Pending = 1,
    Mapped = 2,
    Submitted = 3,
    Completed = 4,
    NeedsReview = 5,
    NeedsAttention = 6,
    NotAnOrder = 7,
    Skipped = 8,
    Failed = 9
}

/// <summary>How a document's intakes were read: by a profile, by a profile the AI proposed and the executor verified, by the AI alone, or as the canonical JSON.</summary>
public enum MappingMethod
{
    None = 0,
    Profile = 1,
    AiVerifiedProfile = 2,
    AiExtraction = 3,
    Canonical = 4
}

/// <summary>One step of a document's trace (design 72 "Inbound store").</summary>
public enum IntakeStep
{
    Received = 1,
    Stored = 2,
    Extracted = 3,
    Fingerprinted = 4,
    ProfileMatched = 5,
    ProfileNotFound = 6,
    ProfileFailed = 7,
    AiRequested = 8,
    AiCompleted = 9,
    AiFailed = 10,
    ProfileProposed = 11,
    ProfileVerified = 12,
    ProfileVerificationFailed = 13,
    NotAnOrder = 14,
    Mapped = 15,
    Submitted = 16,
    OrderCreated = 17,
    NeedsReview = 18,
    Resolved = 19,
    Rejected = 20,
    OrderConfirmed = 21,
    Skipped = 22,
    Failed = 23,
    Reprocessed = 24
}

/// <summary>The outcome icon of a trace step.</summary>
public enum StepOutcome
{
    Info = 1,
    Success = 2,
    Warning = 3,
    Error = 4
}

/// <summary>An import profile's lifecycle: learned and on probation, trusted, or no longer used.</summary>
public enum ImportProfileStatus
{
    Proposed = 1,
    Active = 2,
    Retired = 3
}

/// <summary>Where an import profile's current version came from: learned by the AI, or repaired after a correction.</summary>
public enum ProfileOrigin
{
    Ai = 1,
    Repair = 2
}
