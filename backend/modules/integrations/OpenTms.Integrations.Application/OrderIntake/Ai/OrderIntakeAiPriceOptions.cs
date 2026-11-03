using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenTms.Integrations.OrderIntake.Ai;

/// <summary>
/// The price table of the AI metering (design 72 "Metering", AP-05): per model id the US dollars per million input,
/// output, cache-read and cache-write tokens. Defaults below; "Integrations:OrderIntake:Ai:Prices:{modelId}" overrides or
/// adds a model. A dated model id ("claude-opus-5-5-20261101") takes the price of its longest matching prefix.
/// </summary>
public class OrderIntakeAiPriceOptions
{
    public const string SectionName = "Integrations:OrderIntake:Ai:Prices";

    public Dictionary<string, OrderIntakeAiModelPrice> Models { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude-opus-5-5"] = new() { Input = 4m, Output = 20m, CacheRead = 0.20m, CacheWrite = 5m },
        ["claude-sonnet-5-5"] = new() { Input = 2m, Output = 10m, CacheRead = 0.20m, CacheWrite = 2.50m },
        ["claude-haiku-5-5"] = new() { Input = 0.10m, Output = 0.50m, CacheRead = 0.01m, CacheWrite = 0.125m }
    };

    public OrderIntakeAiModelPrice? Find(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return null;
        }

        if (Models.TryGetValue(modelId, out var price))
        {
            return price;
        }

        return Models
            .Where(kv => modelId.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(kv => kv.Key.Length)
            .Select(kv => kv.Value)
            .FirstOrDefault();
    }

    /// <summary>The cost in millionths of a dollar: tokens × dollars per million tokens is exactly micro-dollars. Unknown model → 0.</summary>
    public long CostMicroUsd(string modelId, LanguageModelUsage usage)
    {
        var price = Find(modelId);
        if (price == null)
        {
            return 0;
        }

        var cost = usage.InputTokens * price.Input + usage.OutputTokens * price.Output
                   + usage.CacheReadTokens * price.CacheRead + usage.CacheWriteTokens * price.CacheWrite;
        return (long)Math.Round(cost, MidpointRounding.AwayFromZero);
    }
}

/// <summary>US dollars per million tokens.</summary>
public class OrderIntakeAiModelPrice
{
    public decimal Input { get; set; }

    public decimal Output { get; set; }

    public decimal CacheRead { get; set; }

    public decimal CacheWrite { get; set; }
}
