namespace OpenTms.Integrations.OrderIntake;

/// <summary>
/// The Integrations localization keys of the trace's step texts (design 72 "Inbound store"). Parameters are ids, counts,
/// formats, file names and short codes — never file content or personal data.
/// </summary>
public static class IntakeStepKeys
{
    private const string Prefix = "Integrations:Step.";

    /// <summary>{channel}</summary>
    public const string Received = Prefix + "Received";

    /// <summary>{sizeBytes}</summary>
    public const string Stored = Prefix + "Stored";

    /// <summary>{maxBytes}</summary>
    public const string DocumentTooLarge = Prefix + "DocumentTooLarge";

    /// <summary>{maxBytes}</summary>
    public const string MessageTooLarge = Prefix + "MessageTooLarge";

    /// <summary>{fileName}</summary>
    public const string Skipped = Prefix + "Skipped";

    /// <summary>{format}, {lineCount}</summary>
    public const string Extracted = Prefix + "Extracted";

    /// <summary>{pageCount}</summary>
    public const string ExtractedScanned = Prefix + "ExtractedScanned";

    /// <summary>{reason}</summary>
    public const string ExtractionFailed = Prefix + "ExtractionFailed";

    /// <summary>{description}, {senderKey}</summary>
    public const string Fingerprinted = Prefix + "Fingerprinted";

    /// <summary>{profileName}, {version}, {intakeCount}</summary>
    public const string ProfileMatched = Prefix + "ProfileMatched";

    public const string ProfileNotFound = Prefix + "ProfileNotFound";

    public const string ProfileNotFoundAiOff = Prefix + "ProfileNotFoundAiOff";

    /// <summary>{profileName}, {version}, {errorCount}, {firstError}</summary>
    public const string ProfileFailed = Prefix + "ProfileFailed";

    /// <summary>{model}, {documentCount}</summary>
    public const string AiRequested = Prefix + "AiRequested";

    /// <summary>{model}, {documentCount}</summary>
    public const string AiCompleted = Prefix + "AiCompleted";

    /// <summary>{reason}</summary>
    public const string AiFailed = Prefix + "AiFailed";

    /// <summary>{attempt}, {maxAttempts}</summary>
    public const string AiFailedRetry = Prefix + "AiFailedRetry";

    /// <summary>{profileName}</summary>
    public const string ProfileProposed = Prefix + "ProfileProposed";

    public const string ProfileVerified = Prefix + "ProfileVerified";

    /// <summary>{differenceCount}</summary>
    public const string ProfileVerificationFailed = Prefix + "ProfileVerificationFailed";

    /// <summary>{rowCount}</summary>
    public const string SpreadsheetTruncated = Prefix + "SpreadsheetTruncated";

    /// <summary>{profileName}, {version}</summary>
    public const string ProfileRepaired = Prefix + "ProfileRepaired";

    /// <summary>{profileName}, {differenceCount}</summary>
    public const string ProfileRepairFailed = Prefix + "ProfileRepairFailed";

    public const string NotAnOrder = Prefix + "NotAnOrder";

    /// <summary>{intakeCount}, {method}</summary>
    public const string Mapped = Prefix + "Mapped";

    public const string NoIntakes = Prefix + "NoIntakes";

    /// <summary>{intakeIndex}, {fuzzyMatches}, {created}</summary>
    public const string Submitted = Prefix + "Submitted";

    /// <summary>{intakeIndex}, {fuzzyMatches}, {created}: Submitted when Orders matched by a similar name or created master data (build note 25).</summary>
    public const string SubmittedWithMasterData = Prefix + "SubmittedWithMasterData";

    /// <summary>{intakeIndex}, {reason}</summary>
    public const string SubmitFailed = Prefix + "SubmitFailed";

    /// <summary>{orderNumber}, {confirmed}</summary>
    public const string OrderCreated = Prefix + "OrderCreated";

    /// <summary>{intakeIndex}, {unresolvedCount}, {unresolved}</summary>
    public const string NeedsReview = Prefix + "NeedsReview";

    /// <summary>{intakeIndex}, {corrected}</summary>
    public const string Resolved = Prefix + "Resolved";

    /// <summary>{intakeIndex}</summary>
    public const string Rejected = Prefix + "Rejected";

    /// <summary>{orderNumber}, {edited}</summary>
    public const string OrderConfirmed = Prefix + "OrderConfirmed";

    public const string Reprocessed = Prefix + "Reprocessed";

    /// <summary>The Integrations localization key of an enum member: <c>Integrations:InboundMessageStatus.Received</c>.</summary>
    public static string EnumKey<TEnum>(TEnum value) where TEnum : struct, System.Enum => $"Integrations:{typeof(TEnum).Name}.{value}";
}
