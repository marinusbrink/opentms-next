using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using OpenTms.Integrations.AiConnector;
using Shouldly;
using Xunit;

namespace OpenTms.ArchitectureTests;

/// <summary>
/// Design 74 "Field allowlists" and "Prompt injection" (risk class High): the tool DTOs stay free of names like <c>Amount</c> outside <c>get_order_rates</c> and <c>get_customer_revenue</c>, and of <c>email</c>,
/// <c>phone</c>, <c>driver</c> and <c>remarks</c> everywhere, so a later field is caught at build; and no tool method maps to a write verb of a service. The marker test on real data is <c>FieldAllowlistHostTests</c>.
/// </summary>
public class AiConnectorToolContractTests
{
    private static readonly Assembly Contracts = typeof(IAiConnectorToolService).Assembly;

    /// <summary>The DTOs of tools design 74 or 75 allows money in.</summary>
    private static readonly HashSet<string> MoneyDtos =
    [
        nameof(AiOrderRates), nameof(AiRateTotals), nameof(AiRateLine), nameof(AiCustomerRevenue), nameof(AiCustomerRevenueTotal),
    ];

    private static readonly string[] MoneyWords = ["amount", "revenue", "cost", "margin", "price", "total"];

    /* "Total" also names a row count and totals of freight: those are named here, one by one. */
    private static readonly HashSet<string> NotMoney =
    [
        nameof(AiToolList<object>) + "." + nameof(AiToolList<object>.Total),
        nameof(AiTripDetail) + "." + nameof(AiTripDetail.TotalQuantity),
        nameof(AiTripDetail) + "." + nameof(AiTripDetail.TotalWeightKg),
        nameof(AiMetricAnswer) + "." + nameof(AiMetricAnswer.TotalGroups),
    ];

    private static readonly string[] PersonalWords = ["email", "phone", "driver", "remark", "mobile", "instruction", "vin", "vat", "chamber", "contact", "pincode", "fullname"];

    private static IEnumerable<Type> ToolDtoTypes()
    {
        var seen = new HashSet<Type>();
        var queue = new Queue<Type>(typeof(IAiConnectorToolService).GetMethods().Select(m => m.ReturnType.GetGenericArguments().Single()));
        while (queue.Count > 0)
        {
            var type = queue.Dequeue();
            var open = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
            if (!seen.Add(open) || open.Namespace != typeof(IAiConnectorToolService).Namespace)
            {
                foreach (var argument in type.GetGenericArguments().Where(a => a.Namespace == typeof(IAiConnectorToolService).Namespace))
                {
                    queue.Enqueue(argument);
                }

                continue;
            }

            yield return open;
            foreach (var argument in type.GetGenericArguments())
            {
                queue.Enqueue(argument);
            }

            foreach (var property in open.GetProperties())
            {
                var propertyType = property.PropertyType;
                queue.Enqueue(propertyType);
                foreach (var argument in propertyType.GetGenericArguments())
                {
                    queue.Enqueue(argument);
                }
            }
        }
    }

    private static string Name(Type type) => type.IsGenericType ? type.Name[..type.Name.IndexOf('`')] : type.Name;

    [Fact]
    public void The_walk_finds_every_tools_dto()
    {
        var names = ToolDtoTypes().Select(Name).ToList();
        foreach (var expected in new[] { nameof(AiOrderRow), nameof(AiOrderDetail), nameof(AiOrderLine), nameof(AiTripRow), nameof(AiTripDetail), nameof(AiTripStop), nameof(AiLegRow), nameof(AiLegEnd), nameof(AiRelationRow), nameof(AiVehicleRow), nameof(AiOrderRates), nameof(AiRateLine), nameof(AiCustomerRevenue), nameof(AiMetricAnswer), nameof(AiMetricList) })
        {
            names.ShouldContain(expected);
        }
    }

    [Fact]
    public void No_tool_dto_outside_the_money_tools_has_a_money_field()
    {
        var violations = ToolDtoTypes()
            .Where(t => !MoneyDtos.Contains(Name(t)))
            .SelectMany(t => t.GetProperties().Select(p => Name(t) + "." + p.Name))
            .Where(field => !NotMoney.Contains(field))
            .Where(field => MoneyWords.Any(word => field[(field.IndexOf('.') + 1)..].Contains(word, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        violations.ShouldBeEmpty("money only through get_order_rates, get_customer_revenue and the metrics (PO decision 3)");
    }

    [Fact]
    public void No_tool_dto_has_a_personal_field()
    {
        var violations = ToolDtoTypes()
            .SelectMany(t => t.GetProperties().Select(p => Name(t) + "." + p.Name))
            .Where(field => PersonalWords.Any(word => field[(field.IndexOf('.') + 1)..].Contains(word, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        violations.ShouldBeEmpty("minimal personal data (PO decision 4)");
    }

    [Fact]
    public void A_tool_result_is_never_a_source_dto()
    {
        ToolDtoTypes().ShouldAllBe(t => t.Namespace == typeof(IAiConnectorToolService).Namespace, "every result is an allowlist of its own");
    }

    [Fact]
    public void There_are_nine_tools_of_design_74_and_two_of_design_75_and_every_method_of_the_service_is_a_tool()
    {
        AiConnectorToolCatalog.All.Count.ShouldBe(11);
        AiConnectorToolCatalog.All.Select(t => t.Name).Distinct().Count().ShouldBe(11);
        typeof(IAiConnectorToolService).GetMethods().Length.ShouldBe(11);
    }

    private static readonly Regex WriteWord = new("^(Create|Update|Delete|Remove|Insert|Add|Set|Save|Confirm|Cancel|Approve|Revise|Archive|Unarchive|Assign|Unassign|Start|Complete|Move|Split|Merge|Copy|Send|Post|Put|Patch|Import|Revoke|Accept|Withdraw|Upsert|Bulk)", RegexOptions.Compiled);

    [Fact]
    public void No_tool_method_has_a_write_verb()
    {
        var methods = typeof(IAiConnectorToolService).GetMethods().Select(m => m.Name).ToList();
        methods.Where(m => WriteWord.IsMatch(m)).ShouldBeEmpty();
        methods.ShouldAllBe(m => m.StartsWith("Search", StringComparison.Ordinal) || m.StartsWith("Get", StringComparison.Ordinal) || m.StartsWith("List", StringComparison.Ordinal) || m.StartsWith("Query", StringComparison.Ordinal));
    }

    [Fact]
    public void The_tool_service_calls_only_read_methods_of_the_services_it_holds()
    {
        var source = SourceOf("modules/integrations/OpenTms.Integrations.Application/AiConnector/AiConnectorToolService.cs");
        var fields = new[] { "_orders", "_trips", "_legs", "_relations", "_vehicles", "_rateLines", "_revenue", "_metrics" };
        var calls = Regex.Matches(source, @"\b(" + string.Join('|', fields) + @")\.(\w+)\(").Select(m => m.Groups[1].Value + "." + m.Groups[2].Value).Distinct().ToList();

        calls.ShouldNotBeEmpty();
        calls.ShouldBe(
            ["_orders.GetListAsync", "_orders.GetAsync", "_trips.GetListAsync", "_trips.GetAsync", "_trips.GetSummaryAsync", "_legs.GetListAsync", "_relations.GetListAsync", "_vehicles.GetListAsync", "_rateLines.GetAsync", "_revenue.GetAsync", "_metrics.ListAsync", "_metrics.QueryAsync"],
            ignoreOrder: true,
            "design 74 'Cross-module synchronous calls': read methods only");

        /* And nothing else reaches a repository or a DbContext. */
        source.ShouldNotContain("IRepository<");
        source.ShouldNotContain("DbContext");
        source.ShouldNotContain("IDistributedEventBus");
    }

    private static string SourceOf(string relative)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "OpenTms.slnx")))
            {
                return File.ReadAllText(Path.Combine(directory.FullName, relative));
            }
        }

        throw new InvalidOperationException("OpenTms.slnx not found above " + AppContext.BaseDirectory);
    }
}
