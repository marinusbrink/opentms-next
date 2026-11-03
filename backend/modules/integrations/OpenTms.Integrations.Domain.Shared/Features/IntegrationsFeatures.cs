namespace OpenTms.Integrations.Features;

public static class IntegrationsFeatures
{
    public const string GroupName = "Integrations";

    /// <summary>Postal-code lookup for addresses (design 44): off until a tenant switches it on (constitution rule 4).</summary>
    public const string AddressLookup = GroupName + ".AddressLookup";

    /// <summary>
    /// Order intake (design 72): mailboxes synchronise, the intake API answers and the import pipeline runs. Off until a
    /// tenant switches it on (constitution rule 4).
    /// </summary>
    public const string OrderIntake = GroupName + ".OrderIntake";

    /// <summary>
    /// A child of <see cref="OrderIntake"/>: documents no import profile reads may be sent to the language model (Anthropic)
    /// to learn their layout. Off by default; switching it on is the tenant's decision (design 72, privacy note).
    /// </summary>
    public const string OrderIntakeAiMapping = OrderIntake + ".AiMapping";
}
