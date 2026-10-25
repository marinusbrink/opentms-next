using System;
using System.Linq;
using System.Threading.Tasks;
using OpenTms.PlanningExecution;
using OpenTms.PlanningExecution.TransportTrips;
using OpenTms.Platform.ReferenceData;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Xunit;

namespace OpenTms.EntityFrameworkCore.PlanningExecution;

/// <summary>TripAttributeDefinitionAppService on SQLite (design 56 "Attributes", design-50 shape): keys, names, positions, lookup, isolation. Risk class: Medium.</summary>
[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class TripAttributeDefinitionAppServiceTests : ReferenceDataAppServiceTestBase
{
    private ITripAttributeDefinitionAppService Definitions => GetRequiredService<ITripAttributeDefinitionAppService>();

    private Task<TripAttributeDefinitionDto> CreateAsync(string key, string en, string? nl = null, int position = 1, bool archived = false)
        => Definitions.CreateAsync(new TripAttributeDefinitionCreateDto { Key = key, Names = Names(en, nl), Position = position, IsArchived = archived });

    [Fact]
    public async Task Create_validates_the_key_shape_and_uniqueness_and_the_list_sorts_by_position_and_searches_key_and_name()
    {
        using var _ = InTenantA();
        using var culture = UiCulture("nl-NL");
        var colour = await CreateAsync("Colour", "Colour", "Kleur", 2);
        var door = await CreateAsync("Door", "Door", "Deur", 1);
        colour.Name.ShouldBe("Kleur");
        colour.Names.ShouldBe(new LocalizedNameDto { ["en"] = "Colour", ["nl"] = "Kleur" });

        (await Should.ThrowAsync<BusinessException>(() => CreateAsync("Colour", "Twice"))).Code.ShouldBe(ReferenceDataErrorCodes.DuplicateKey, "the key is unique as typed");
        (await Should.ThrowAsync<BusinessException>(() => CreateAsync("1bad", "Bad"))).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
        (await Should.ThrowAsync<BusinessException>(() => CreateAsync("has space", "Bad"))).Code.ShouldBe(ReferenceDataErrorCodes.InvalidCode);
        await Should.ThrowAsync<Volo.Abp.Validation.AbpValidationException>(() => CreateAsync("Key", "Bad", position: 0));

        (await Definitions.GetListAsync(List())).Rows.Select(r => r.Key).ShouldBe(["Door", "Colour"]);
        (await Definitions.GetListAsync(List(search: "kleur"))).Rows.ShouldHaveSingleItem().Key.ShouldBe("Colour");
        (await Definitions.GetListAsync(List(sortBy: "key", sortDir: "desc"))).Rows.Select(r => r.Key).ShouldBe(["Door", "Colour"]);
        (await Definitions.GetLookupAsync()).Select(l => l.Code).ShouldBe(["Door", "Colour"]);
        door.Position.ShouldBe(1);
    }

    [Fact]
    public async Task Update_archive_delete_and_isolation()
    {
        Guid id;
        using (InTenantA())
        {
            var created = await CreateAsync("Colour", "Colour");
            var updated = await Definitions.UpdateAsync(created.Id, new TripAttributeDefinitionUpdateDto { Key = "Color", Names = Names("Color"), Position = 5, IsArchived = true, ConcurrencyStamp = created.ConcurrencyStamp });
            updated.Key.ShouldBe("Color");
            updated.Position.ShouldBe(5);
            (await Definitions.GetListAsync(List())).Rows.ShouldBeEmpty("archived rows are hidden");
            (await Definitions.GetLookupAsync()).ShouldBeEmpty();
            (await Definitions.GetListAsync(List(includeArchived: true))).Rows.ShouldHaveSingleItem();
            await Should.ThrowAsync<Volo.Abp.Data.AbpDbConcurrencyException>(() => Definitions.UpdateAsync(created.Id, new TripAttributeDefinitionUpdateDto { Key = "Color", Names = Names("Color"), ConcurrencyStamp = created.ConcurrencyStamp }));
            id = created.Id;
        }

        using (InTenantB())
        {
            (await Definitions.GetListAsync(List(includeArchived: true))).Rows.ShouldBeEmpty();
            await Should.ThrowAsync<EntityNotFoundException>(() => Definitions.GetAsync(id));
            (await CreateAsync("Color", "Color")).Key.ShouldBe("Color", "keys are unique per tenant");
        }

        using (InTenantA())
        {
            await Definitions.DeleteAsync(id);
            await Should.ThrowAsync<EntityNotFoundException>(() => Definitions.GetAsync(id));
        }
    }
}
