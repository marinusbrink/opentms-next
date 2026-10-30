using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Services;

/* Probe types of the background-path guards in AuthorizationCoverageTests. They live in the test assembly only (the
 * guards scan the module assemblies), and the interface sits in an OpenTms.*.Published namespace like the real
 * cross-module readers, so the guards can be shown to report what they must. */

namespace OpenTms.ArchitectureTests.Probes.Published
{
    public interface IProbeReader
    {
        Task GetAsync();
    }
}

namespace OpenTms.ArchitectureTests.Probes
{
    using OpenTms.ArchitectureTests.Probes.Published;

    /// <summary>The bug of design 56's browser run: a published reader that requires a user.</summary>
    [RemoteService(IsEnabled = false)]
    [Authorize]
    public class AuthorizedProbeReader : ApplicationService, IProbeReader
    {
        public Task GetAsync() => Task.CompletedTask;
    }

    /// <summary>The same bug on a method only.</summary>
    [RemoteService(IsEnabled = false)]
    public class MethodAuthorizedProbeReader : ApplicationService, IProbeReader
    {
        [Authorize]
        public Task GetAsync() => Task.CompletedTask;
    }

    /// <summary>The intended shape: in-process and anonymous.</summary>
    [RemoteService(IsEnabled = false)]
    [AllowAnonymous]
    public class AnonymousProbeReader : ApplicationService, IProbeReader
    {
        public Task GetAsync() => Task.CompletedTask;
    }

    /// <summary>An anonymous service the conventional controllers would put on HTTP.</summary>
    [AllowAnonymous]
    public class RemoteAnonymousProbeAppService : ApplicationService
    {
        public Task DoAsync() => Task.CompletedTask;
    }

    /// <summary>The same leak on one method of an otherwise authorized service.</summary>
    [Authorize]
    public class MethodAnonymousProbeAppService : ApplicationService
    {
        [AllowAnonymous]
        public Task DoAsync() => Task.CompletedTask;
    }
}
