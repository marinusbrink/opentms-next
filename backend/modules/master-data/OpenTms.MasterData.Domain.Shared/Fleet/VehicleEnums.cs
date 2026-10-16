namespace OpenTms.MasterData.Fleet;

/// <summary>
/// The fuel or drive of a vehicle (design 48). Null on the vehicle means unknown; NotApplicable is for
/// trailers. OpenTMS's "Unknow" value is dropped.
/// </summary>
public enum FuelType
{
    Diesel = 1,
    Petrol = 2,
    Lng = 3,
    Cng = 4,
    Lpg = 5,
    Hvo = 6,
    Electric = 7,
    Hydrogen = 8,
    DieselElectricHybrid = 9,
    PetrolElectricHybrid = 10,
    Other = 11,
    NotApplicable = 12
}

/// <summary>The exhaust emission class (design 48, assessment §5: an enum instead of the free text).</summary>
public enum EuroClass
{
    Euro0 = 1,
    Euro1 = 2,
    Euro2 = 3,
    Euro3 = 4,
    Euro4 = 5,
    Euro5 = 6,
    Euro6 = 7,
    Eev = 8,
    ZeroEmission = 9
}

/// <summary>Where a characteristic of a vehicle comes from: its type (read-only on the vehicle) or the vehicle itself.</summary>
public enum VehicleCharacteristicOrigin
{
    Type = 1,
    Vehicle = 2
}
