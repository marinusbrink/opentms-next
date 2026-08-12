using Xunit;

namespace OpenTms.EntityFrameworkCore;

[CollectionDefinition(OpenTmsTestConsts.CollectionDefinitionName)]
public class OpenTmsEntityFrameworkCoreCollection : ICollectionFixture<OpenTmsEntityFrameworkCoreFixture>
{

}
