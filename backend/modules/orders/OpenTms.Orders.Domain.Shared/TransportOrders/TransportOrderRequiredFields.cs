using System;

namespace OpenTms.Orders.TransportOrders;

/// <summary>
/// The fields an order of a transport order type must have filled (design 51, assessment §3.9: an enum set instead of
/// OpenTMS's list of reflected property names). Enforced by the order design; additive.
/// </summary>
[Flags]
public enum TransportOrderRequiredFields
{
    None = 0,
    CustomerReference = 1,
    LoadingReference = 2,
    UnloadingReference = 4,
    LoadingTimeWindow = 8,
    UnloadingTimeWindow = 16,
    ProductPerLine = 32,
    TransportUnitPerLine = 64,
    QuantityPerLine = 128,
    WeightPerLine = 256,
    VolumePerLine = 512,
    LoadingMetersPerLine = 1024,
    Temperature = 2048
}

public static class TransportOrderRequiredFieldsExtensions
{
    /// <summary>Every defined flag together; a value with another bit is refused.</summary>
    public const TransportOrderRequiredFields All = (TransportOrderRequiredFields)4095;

    public static bool IsValid(this TransportOrderRequiredFields fields) => (fields & ~All) == 0;
}
