namespace OpenTms.Integrations.OrderIntake.Ai;

/// <summary>The AI settings of order intake (design 72, host tab Integrations): deployment defaults from "Integrations:OrderIntake:Ai", overlaid by the host's saved settings.</summary>
public class OrderIntakeAiOptions
{
    public const string SectionName = "Integrations:OrderIntake:Ai";

    public string? ApiKey { get; set; }

    public string LearningModel { get; set; } = "claude-opus-5-5";

    public string ExtractionModel { get; set; } = "claude-opus-5-5";
}
