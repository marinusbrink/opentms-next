using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTms.Integrations.AddressLookup;
using Shouldly;
using Volo.Abp.Caching;
using Volo.Abp.Modularity;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Integrations;

/// <summary>
/// The suggestion-provider chain of the postal-code dropdown (PO 2026-10-13, extension of design 44) with two
/// recording sources for the made-up country "ZZ" registered after the production wiring: the first source
/// that supports the country answers, the second is never asked; the three-character threshold counts
/// non-space characters; the term reaches the source trimmed, with a maximum of ten; a source that throws gives
/// no lines and a log line without the term. PDOK cannot show these: it refuses any term that is no postal-code
/// shape before it sends a request. Risk class: High (external integration); the log rule is Critical (rule 5).
/// </summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class AddressSuggestionChainTests : OpenTmsTestBase<AddressSuggestionChainTestModule>
{
    private readonly IAddressLookupAppService _service;
    private readonly ProbeSources _probes;
    private readonly FakeHttpMessageHandler _transport;
    private readonly CapturingLog _log;
    private readonly IDistributedCache<AddressSuggestionsCacheItem> _cache;

    public AddressSuggestionChainTests()
    {
        _service = GetRequiredService<IAddressLookupAppService>();
        _probes = GetRequiredService<ProbeSources>();
        _transport = GetRequiredService<FakeHttpMessageHandler>();
        _log = GetRequiredService<CapturingLog>();
        _cache = GetRequiredService<IDistributedCache<AddressSuggestionsCacheItem>>();
    }

    private static AddressSuggestionDto Line(string postalCode) => new() { Label = postalCode + " Somewhere", PostalCode = postalCode, City = "Somewhere" };

    [Fact]
    public void The_probes_come_after_PDOK_in_registration_order()
    {
        GetRequiredService<IEnumerable<IAddressSuggestionProvider>>().Select(p => p.Name).ShouldBe(["pdok", "probe-first", "probe-second"]);
    }

    [Fact]
    public async Task The_first_source_that_supports_the_country_answers_and_the_next_is_never_asked()
    {
        _probes.First.Answer = [Line("ZZ-1"), Line("ZZ-2")];
        _probes.Second.Answer = [Line("never")];

        var lines = await _service.GetSuggestionsAsync("zz", "abc");

        lines.Select(l => l.PostalCode).ShouldBe(["ZZ-1", "ZZ-2"]);
        _probes.Second.Calls.ShouldBeEmpty();
        _transport.Requests.ShouldBeEmpty("PDOK does not support ZZ");
    }

    [Fact]
    public async Task The_source_gets_the_upper_case_country_the_trimmed_term_and_a_maximum_of_ten()
    {
        _probes.First.Answer = [Line("ZZ-1")];

        await _service.GetSuggestionsAsync(" zz ", "  a b c  ");

        var call = _probes.First.Calls.ShouldHaveSingleItem();
        call.Country.ShouldBe("ZZ");
        call.Term.ShouldBe("a b c", "trimmed; the inner spaces are the source's to interpret");
        call.Max.ShouldBe(10);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("a b c")]
    [InlineData(" a  b c ")]
    [InlineData("1 2 3")]
    public async Task Three_characters_spaces_ignored_reach_the_source(string term)
    {
        _probes.First.Answer = [Line("ZZ-1")];

        (await _service.GetSuggestionsAsync("ZZ", term)).ShouldHaveSingleItem();

        _probes.First.Calls.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("a b")]
    [InlineData(" a     b ")]
    [InlineData("1 2")]
    public async Task Two_characters_spaces_ignored_do_not(string term)
    {
        _probes.First.Answer = [Line("ZZ-1")];

        (await _service.GetSuggestionsAsync("ZZ", term)).ShouldBeEmpty();

        _probes.First.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_country_no_source_supports_gives_no_lines()
    {
        (await _service.GetSuggestionsAsync("YY", "abcdef")).ShouldBeEmpty();

        _probes.First.Calls.ShouldBeEmpty();
        _probes.Second.Calls.ShouldBeEmpty();
        _transport.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_throwing_source_gives_no_lines_and_a_warning_with_its_name_and_the_country_but_not_the_term()
    {
        _probes.First.Throw = new InvalidOperationException("source broke");

        var lines = await _service.GetSuggestionsAsync("ZZ", "Secret Street 42");

        lines.ShouldBeEmpty();
        _probes.Second.Calls.ShouldBeEmpty("a failing source is not a reason to ask the next");
        var entry = _log.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Warning);
        entry.Category.ShouldBe(typeof(AddressLookupAppService).FullName);
        entry.Message.ShouldContain("probe-first");
        entry.Message.ShouldContain("ZZ");
        entry.Everything.ShouldNotContain("Secret", Case.Insensitive, "no address data in the log (rule 5)");
        entry.Everything.ShouldNotContain("42", Case.Insensitive, "no address data in the log (rule 5)");
        entry.Exception.ShouldNotBeNull();
        entry.Exception.ShouldContain("source broke");
        (await _cache.GetAsync("ZZ|SECRETSTREET42")).ShouldBeNull("a failure is not cached");
    }

    [Fact]
    public async Task A_cancelled_source_is_a_failure_too_never_an_error_for_the_user()
    {
        _probes.First.Throw = new OperationCanceledException("timed out");

        (await _service.GetSuggestionsAsync("ZZ", "abc")).ShouldBeEmpty();

        _log.Entries.ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Warning);
    }

    [Fact]
    public async Task Lines_are_cached_under_the_upper_case_key_without_spaces_and_empty_lists_are_not()
    {
        _probes.First.Answer = [Line("ZZ-1")];
        await _service.GetSuggestionsAsync("zz", "a b c");
        await _service.GetSuggestionsAsync("ZZ", "ABC");
        await _service.GetSuggestionsAsync("Zz", " abc ");

        _probes.First.Calls.Count.ShouldBe(1);
        (await _cache.GetAsync("ZZ|ABC")).ShouldNotBeNull();

        _probes.First.Answer = [];
        await _service.GetSuggestionsAsync("ZZ", "xyz");
        await _service.GetSuggestionsAsync("ZZ", "xyz");

        _probes.First.Calls.Count.ShouldBe(3, "an empty list is asked again");
        (await _cache.GetAsync("ZZ|XYZ")).ShouldBeNull();
    }
}

/// <summary>One recorded call of a probe source.</summary>
public sealed record ProbeCall(string Country, string Term, int Max);

/// <summary>A suggestion source for the made-up country "ZZ" that records its calls and answers what the test sets.</summary>
public sealed class ProbeSuggestionProvider : IAddressSuggestionProvider
{
    public ProbeSuggestionProvider(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public List<ProbeCall> Calls { get; } = new();

    public List<AddressSuggestionDto> Answer { get; set; } = new();

    public Exception? Throw { get; set; }

    public bool Supports(string countryCode) => countryCode == "ZZ";

    public Task<List<AddressSuggestionDto>> SuggestAsync(string countryCode, string term, int max, CancellationToken cancellationToken)
    {
        Calls.Add(new ProbeCall(countryCode, term, max));
        if (Throw != null)
        {
            throw Throw;
        }

        return Task.FromResult(Answer.ToList());
    }
}

/// <summary>The two probes of one test application.</summary>
public sealed class ProbeSources
{
    public ProbeSuggestionProvider First { get; } = new("probe-first");

    public ProbeSuggestionProvider Second { get; } = new("probe-second");
}

/// <summary>The address-lookup test application plus two recording suggestion sources after PDOK.</summary>
[DependsOn(typeof(AddressLookupTestModule))]
public class AddressSuggestionChainTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var probes = new ProbeSources();
        context.Services.AddSingleton(probes);
        context.Services.AddSingleton<IAddressSuggestionProvider>(probes.First);
        context.Services.AddSingleton<IAddressSuggestionProvider>(probes.Second);
    }
}
