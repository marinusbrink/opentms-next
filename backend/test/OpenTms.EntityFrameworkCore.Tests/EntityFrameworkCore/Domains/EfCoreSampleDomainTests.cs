using OpenTms.Samples;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Domains;

[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class EfCoreSampleDomainTests : SampleDomainTests<OpenTmsEntityFrameworkCoreTestModule>
{

}
