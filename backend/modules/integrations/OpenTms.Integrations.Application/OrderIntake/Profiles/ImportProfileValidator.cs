using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenTms.Orders.Intake;

namespace OpenTms.Integrations.OrderIntake.Profiles;

/// <summary>
/// The validation of a profile run (design 72 §4 "Validation"): every intake has a customer reference or a customer, at
/// least one line, and per line a Pickup and a Delivery stop each with a postal code or city and a date; dates and times
/// parse; numbers are ≥ 0 (the field table refuses negative numbers when writing). A stop without its own date passes
/// when the intake has an order date: Orders' resolver gives such a stop the order date (design 72 "Resolution", Dates).
/// A run that fails on a matched profile is layout drift: ProfileFailed, and the document goes to the AI path.
/// </summary>
public static class ImportProfileValidator
{
    public const string Required = "required";
    public const string NoIntakes = "noIntakes";

    public static List<ProfileRunError> Validate(IReadOnlyList<OrderIntakeDto> intakes)
    {
        var errors = new List<ProfileRunError>();
        if (intakes.Count == 0)
        {
            errors.Add(new ProfileRunError(null, NoIntakes));
            return errors;
        }

        for (var i = 0; i < intakes.Count; i++)
        {
            var intake = intakes[i];
            var prefix = $"intakes[{i}].";
            if (string.IsNullOrWhiteSpace(intake.CustomerReference) && !HasCustomer(intake.Customer))
            {
                errors.Add(new ProfileRunError(prefix + "order.customerReference", Required));
            }

            if (!IsDate(intake.OrderDate, allowEmpty: true))
            {
                errors.Add(new ProfileRunError(prefix + "order.orderDate", IntakeValuePaths.InvalidDate));
            }

            if (intake.Lines.Count == 0)
            {
                errors.Add(new ProfileRunError(prefix + "lines", Required));
                continue;
            }

            for (var l = 0; l < intake.Lines.Count; l++)
            {
                var line = intake.Lines[l];
                foreach (var role in new[] { IntakeStopRole.Pickup, IntakeStopRole.Delivery })
                {
                    var index = line.Stops.FindIndex(s => s.Role == role);
                    if (index < 0)
                    {
                        errors.Add(new ProfileRunError($"{prefix}lines[{l}].stops[{role}]", Required));
                        continue;
                    }

                    var stop = line.Stops[index];
                    var path = $"{prefix}lines[{l}].stops[{index}]";
                    if (string.IsNullOrWhiteSpace(stop.Address.PostalCode) && string.IsNullOrWhiteSpace(stop.Address.City))
                    {
                        errors.Add(new ProfileRunError(path + ".address.postalCode", Required));
                    }

                    if (string.IsNullOrWhiteSpace(stop.Date) && string.IsNullOrWhiteSpace(intake.OrderDate))
                    {
                        errors.Add(new ProfileRunError(path + ".date", Required));
                    }
                    else if (!IsDate(stop.Date, allowEmpty: true))
                    {
                        errors.Add(new ProfileRunError(path + ".date", IntakeValuePaths.InvalidDate));
                    }

                    if (!IsTime(stop.TimeFrom) || !IsTime(stop.TimeTill))
                    {
                        errors.Add(new ProfileRunError(path + ".timeFrom", IntakeValuePaths.InvalidTime));
                    }
                }

                if (line.Quantity < 0 || line.Colli < 0 || line.Positions < 0 || line.WeightKg < 0 || line.VolumeM3 < 0 || line.LoadingMeters < 0
                    || line.LengthCm < 0 || line.WidthCm < 0 || line.HeightCm < 0)
                {
                    errors.Add(new ProfileRunError($"{prefix}lines[{l}]", IntakeValuePaths.NegativeNumber));
                }
            }
        }

        return errors;
    }

    private static bool HasCustomer(OrderIntakeCustomerDto? customer)
        => customer != null && (customer.RelationId != null || !string.IsNullOrWhiteSpace(customer.Name) || !string.IsNullOrWhiteSpace(customer.Code)
                                || !string.IsNullOrWhiteSpace(customer.EmailAddress) || !string.IsNullOrWhiteSpace(customer.VatNumber)
                                || !string.IsNullOrWhiteSpace(customer.ChamberOfCommerceNumber));

    private static bool IsDate(string? value, bool allowEmpty)
        => string.IsNullOrWhiteSpace(value) ? allowEmpty : DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private static bool IsTime(string? value)
        => string.IsNullOrWhiteSpace(value) || TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}

/// <summary>One field where the AI's intakes and the executor's run differ (design 72 §5 "Verification").</summary>
public sealed record IntakeDifference(string Path, string? ModelValue, string? ExecutorValue);

/// <summary>
/// Compares intakes field by field after normalising numbers, dates and whitespace (the field table writes canonical
/// numbers and dates; texts are compared with collapsed whitespace, case-insensitively).
/// </summary>
public static class IntakeComparison
{
    /// <param name="truncated">The model saw only part of the document (a spreadsheet's first rows): the executor may read
    /// more intakes, and the model's last intake may be cut off at the boundary, so it is not compared.</param>
    public static List<IntakeDifference> Compare(IReadOnlyList<OrderIntakeDto> model, IReadOnlyList<OrderIntakeDto> executor, bool truncated)
    {
        var differences = new List<IntakeDifference>();
        var compareCount = model.Count;
        if (truncated)
        {
            if (executor.Count < model.Count)
            {
                differences.Add(new IntakeDifference("intakes.count", model.Count.ToString(CultureInfo.InvariantCulture), executor.Count.ToString(CultureInfo.InvariantCulture)));
            }

            if (executor.Count > model.Count && compareCount > 1)
            {
                compareCount--;
            }
        }
        else if (model.Count != executor.Count)
        {
            differences.Add(new IntakeDifference("intakes.count", model.Count.ToString(CultureInfo.InvariantCulture), executor.Count.ToString(CultureInfo.InvariantCulture)));
        }

        for (var i = 0; i < Math.Min(compareCount, executor.Count); i++)
        {
            differences.AddRange(CompareOne(model[i], executor[i], $"intakes[{i}]."));
        }

        return differences;
    }

    public static IEnumerable<IntakeDifference> CompareOne(OrderIntakeDto model, OrderIntakeDto executor, string prefix = "")
    {
        var left = IntakeValuePaths.Flatten(model);
        var right = IntakeValuePaths.Flatten(executor);
        foreach (var path in left.Keys.Union(right.Keys).OrderBy(p => p, StringComparer.Ordinal))
        {
            var a = left.GetValueOrDefault(path);
            var b = right.GetValueOrDefault(path);
            if (!string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase))
            {
                yield return new IntakeDifference(prefix + path, a, b);
            }
        }
    }

    private static string Normalize(string? value)
        => string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
