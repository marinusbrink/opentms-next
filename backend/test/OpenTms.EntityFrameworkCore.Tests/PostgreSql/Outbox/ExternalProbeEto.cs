using System;

namespace ThirdParty.Probe;

/// <summary>Not an OpenTms.* type: bypasses the outbox like ABP's own events do.</summary>
public class ExternalProbeEto
{
    public Guid ProbeId { get; set; }
}
