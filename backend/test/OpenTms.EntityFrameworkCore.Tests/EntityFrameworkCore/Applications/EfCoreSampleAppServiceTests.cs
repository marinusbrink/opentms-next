using OpenTms.Samples;
using Xunit;

namespace OpenTms.EntityFrameworkCore.Applications;

[Collection(OpenTmsTestConsts.CollectionDefinitionName)]
public class EfCoreSampleAppServiceTests : SampleAppServiceTests<OpenTmsEntityFrameworkCoreTestModule>
{

}
