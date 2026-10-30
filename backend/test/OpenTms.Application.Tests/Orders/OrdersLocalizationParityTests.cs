using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenTms.Orders.ConsignmentNotes;
using OpenTms.Orders.Permissions;
using OpenTms.Orders.TransportOrders;
using Shouldly;
using Xunit;

namespace OpenTms.Orders.Localization;

/// <summary>
/// Key-parity test over the Orders localization resource (design 58, test risk analysis "Localization parity nl/en",
/// Low): every key in en.json exists in nl.json and vice versa, no text is empty, and every error code, permission, enum
/// value and approval message the server uses has a text in both languages.
/// </summary>
public class OrdersLocalizationParityTests
{
    private static Dictionary<string, string> Read(string culture)
    {
        var path = Path.Combine(FindRepositoryRoot(), "backend", "modules", "orders", "OpenTms.Orders.Domain.Shared", "Localization", "Orders", culture + ".json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("texts").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "backend", "OpenTms.slnx")))
        {
            directory = directory.Parent;
        }

        directory.ShouldNotBeNull("the test must run inside the repository");
        return directory.FullName;
    }

    [Fact]
    public void nl_and_en_have_the_same_keys_and_no_empty_texts()
    {
        var en = Read("en");
        var nl = Read("nl");

        nl.Keys.Except(en.Keys).ShouldBeEmpty("keys only in nl.json");
        en.Keys.Except(nl.Keys).ShouldBeEmpty("keys only in en.json");
        en.Where(p => string.IsNullOrWhiteSpace(p.Value)).Select(p => p.Key).ShouldBeEmpty();
        nl.Where(p => string.IsNullOrWhiteSpace(p.Value)).Select(p => p.Key).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("nl")]
    public void Every_error_code_permission_enum_value_and_approval_message_has_a_text(string culture)
    {
        var texts = Read(culture);
        var expected = new List<string> { "Permission:Orders", "Orders:NotFound", "Orders:Setting.ActiveApprovalRules", "Orders:Setting.ActiveApprovalRulesDescription", "Orders:TemplateFromOrderName", "Orders:DefaultProfileCannotBeDeleted", "Orders:FixedDescriptionUsedByProfile" };
        expected.AddRange(OrdersPermissions.All.Select(p => "Permission:" + p["Orders.".Length..]));
        expected.AddRange(OrdersErrorCodes.ConflictCodes.Concat(OrdersErrorCodes.BadRequestCodes).Append(OrdersErrorCodes.RenderTimedOut));
        expected.AddRange(Enum.GetValues<TransportOrderStatus>().Select(v => $"Orders:TransportOrderStatus.{v}"));
        expected.AddRange(Enum.GetValues<OrderSource>().Select(v => $"Orders:OrderSource.{v}"));
        expected.AddRange(Enum.GetValues<ExecutionProgress>().Select(v => $"Orders:ExecutionProgress.{v}"));
        expected.AddRange(Enum.GetValues<PaymentInstruction>().Select(v => $"Orders:PaymentInstruction.{v}"));
        expected.AddRange(Enum.GetValues<MeasurementKind>().Select(v => $"Orders:MeasurementKind.{v}"));
        expected.AddRange(Enum.GetValues<ApprovalRule>().Select(v => $"Orders:ApprovalRule.{v}"));
        expected.AddRange(Enum.GetValues<ApprovalRule>().Select(v => $"Orders:Approval.{v}"));
        expected.AddRange(Enum.GetValues<ApprovalSeverity>().Select(v => $"Orders:ApprovalSeverity.{v}"));
        expected.AddRange(Enum.GetValues<OrderAttributeTarget>().Select(v => $"Orders:OrderAttributeTarget.{v}"));
        expected.AddRange(Enum.GetValues<TemplateRecurrence>().Select(v => $"Orders:TemplateRecurrence.{v}"));
        expected.AddRange(Enum.GetValues<RecurrenceDays>().Where(v => v != RecurrenceDays.None).Select(v => $"Orders:RecurrenceDays.{v}"));
        expected.AddRange(Enum.GetValues<DiscountOrSurchargeSource>().Select(v => $"Orders:DiscountOrSurchargeSource.{v}"));
        expected.AddRange(Enum.GetValues<OrderRemarkKind>().Select(v => $"Orders:OrderRemarkKind.{v}"));
        expected.AddRange(Enum.GetValues<TransportOrderView>().Select(v => $"Orders:TransportOrderView.{v}"));
        expected.AddRange(Enum.GetValues<CmrPartySource>().Select(v => $"Orders:CmrPartySource.{v}"));
        expected.AddRange(Enum.GetValues<CmrGoodsColumn>().Select(v => $"Orders:CmrGoodsColumn.{v}"));
        expected.AddRange(Enum.GetValues<CmrInstructionItem>().Select(v => $"Orders:CmrInstructionItem.{v}"));
        expected.AddRange(Enum.GetValues<CmrRemarkItem>().Select(v => $"Orders:CmrRemarkItem.{v}"));

        expected.Where(key => !texts.ContainsKey(key)).ShouldBeEmpty($"{culture}.json");
        texts[OrdersErrorCodes.RequiredFieldMissing].ShouldContain("{field}");
        texts[OrdersErrorCodes.InvalidTransition].ShouldContain("{from}");
        texts[OrdersErrorCodes.LineNeedsTwoStops].ShouldContain("{position}");
    }
}
