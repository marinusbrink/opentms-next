using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenTms.Platform.DocumentTypes;
using OpenTms.Platform.EmailTemplates;
using OpenTms.Platform.Features;
using OpenTms.Platform.NumberSequences;
using OpenTms.Platform.Permissions;
using OpenTms.Platform.ReportDefinitions;
using Shouldly;
using Xunit;

namespace OpenTms.Platform.PlatformMasterData;

/// <summary>
/// Key parity of the Platform localization resource (nl, en) and the texts the Platform master data needs
/// (docs/designs/49-platform-master-data.md, "Errors (PlatformErrorCodes, localized nl/en)", the sixteen permissions, the
/// five features, the enum labels the editors show, the in-use reason). Risk class: Low (texts).
/// </summary>
public class PlatformLocalizationParityTests
{
    private static Dictionary<string, string> Read(string culture)
    {
        var path = Path.Combine(FindRepositoryRoot(), "backend", "modules", "platform", "OpenTms.Platform.Domain.Shared", "Localization", "Platform", culture + ".json");
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
    public void Every_master_data_error_code_permission_feature_and_enum_value_has_a_text(string culture)
    {
        var texts = Read(culture);

        var expected = new List<string>(PlatformErrorCodes.ConflictCodes.Concat(PlatformErrorCodes.BadRequestCodes))
        {
            "Platform:InUseByNumbers",
            "Feature:" + AppFeatures.MasterData.Platform.Default,
            "Platform:Nav.Platform",
        };
        foreach (var (entity, _, _, _, _) in PlatformPermissions.MasterData.All)
        {
            expected.Add($"Permission:{entity}");
            expected.Add($"Permission:{entity}.Create");
            expected.Add($"Permission:{entity}.Update");
            expected.Add($"Permission:{entity}.Delete");
        }

        foreach (var feature in new[]
                 {
                     AppFeatures.MasterData.Platform.Default, AppFeatures.MasterData.Platform.EmailTemplates, AppFeatures.MasterData.Platform.ReportDefinitions,
                     AppFeatures.MasterData.Platform.DocumentTypes, AppFeatures.MasterData.Platform.NumberSequences
                 })
        {
            expected.Add("Feature:" + feature);
            expected.Add("Feature:" + feature + ".Description");
        }

        expected.AddRange(Enum.GetValues<EmailTemplatePurpose>().Select(v => $"Platform:EmailTemplatePurpose.{v}"));
        expected.AddRange(Enum.GetValues<ReportPurpose>().Select(v => $"Platform:ReportPurpose.{v}"));
        expected.AddRange(Enum.GetValues<ReportRenderingProvider>().Select(v => $"Platform:ReportRenderingProvider.{v}"));
        expected.AddRange(Enum.GetValues<DocumentTransformationKind>().Select(v => $"Platform:DocumentTransformationKind.{v}"));
        expected.AddRange(Enum.GetValues<NumberSequenceScheme>().Select(v => $"Platform:NumberSequenceScheme.{v}"));

        expected.Where(key => !texts.ContainsKey(key)).ShouldBeEmpty($"{culture}.json");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("nl")]
    public void The_error_messages_name_the_data_the_services_put_on_the_exception(string culture)
    {
        /* The services call WithData with these names; a placeholder without data shows as "{field}" to the user. */
        var texts = Read(culture);
        texts[PlatformErrorCodes.DuplicateKey].ShouldContain("{field}");
        texts[PlatformErrorCodes.DuplicateKey].ShouldContain("{value}");
        texts[PlatformErrorCodes.InUse].ShouldContain("{reason}");
        /* The readable field ("HTML body", "From"); the field id stays in the data for clients. */
        texts[PlatformErrorCodes.TemplateSyntaxError].ShouldContain("{fieldLabel}");
        texts[PlatformErrorCodes.TemplateSyntaxError].ShouldContain("{line}");
        texts[PlatformErrorCodes.UnsupportedCulture].ShouldContain("{culture}");
        texts[PlatformErrorCodes.InvalidRegex].ShouldContain("{kind}");
        texts[PlatformErrorCodes.TransformationArgumentRequired].ShouldContain("{kind}");
        texts[PlatformErrorCodes.DuplicateTransformation].ShouldContain("{kind}");
        texts[PlatformErrorCodes.UnknownSortColumn].ShouldContain("{sortBy}");
        texts[PlatformErrorCodes.TooManyRows].ShouldContain("{max}");
    }

    [Theory]
    [InlineData("en", "From", "Send me a copy", "All languages")]
    [InlineData("nl", "Van", "Stuur mij een kopie", "Alle talen")]
    public void The_email_template_editor_has_its_labels_and_the_per_language_content_keys_are_gone(string culture, string from, string sendMeACopy, string allLanguages)
    {
        /* PO 2026-10-18: one template per language, one mail-shaped dialog (design 49, "EmailTemplate"). */
        var texts = Read(culture);
        texts["Platform:From"].ShouldBe(from);
        texts["Platform:SendMeACopy"].ShouldBe(sendMeACopy);
        texts["Platform:AllLanguages"].ShouldBe(allLanguages);

        /* The labels the editor shows, and the ones ScribanTemplateValidator builds for a refusal ("Platform:" + the
         * capitalized field id; the sender address uses Platform:From). */
        foreach (var key in new[]
                 {
                     "Platform:Culture", "Platform:Language.en", "Platform:Language.nl", "Platform:ReplyTo", "Platform:To", "Platform:ToEmptyHint",
                     "Platform:Cc", "Platform:Bcc", "Platform:Subject", "Platform:HtmlBody", "Platform:Tab.General"
                 })
        {
            texts.ShouldContainKey(key, culture);
        }

        foreach (var removed in new[]
                 {
                     "Platform:SenderAddress", "Platform:SenderName", "Platform:SendCopyToSender", "Platform:Tab.Addresses", "Platform:Cultures",
                     "Platform:DefaultLanguageContentRequired", PlatformErrorCodes.Namespace + ":DefaultLanguageContentRequired"
                 })
        {
            texts.ShouldNotContainKey(removed, culture);
        }
    }
}
