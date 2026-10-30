using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenTms.MasterData.Fleet;
using OpenTms.MasterData.Transport;

namespace OpenTms.MasterData.TestData;

/// <summary>What a product, temperature or transport unit is used for: the incompatibility pairs are drawn from it.</summary>
internal enum GoodsKind
{
    General,
    Food,
    Chemical
}

internal sealed record CharacteristicTemplate(string Code, string Name);

internal sealed record VehicleTypeTemplate(
    string Name,
    VehicleTypeKind Kind,
    double? MaxEpq,
    double? MaxLoadingMeters,
    double? LengthCm,
    double? WidthCm,
    double? HeightCm,
    double? GrossVehicleWeightKg,
    double? EmptyWeightKg,
    double? MaxPayloadKg,
    decimal CostPricePerKm,
    FuelType? Fuel,
    string[] Characteristics);

internal sealed record TransportUnitTemplate(
    string Name,
    double? LengthCm,
    double? WidthCm,
    double? HeightCm,
    double? TareWeightKg,
    double? MaxLoadKg,
    bool IsStackable,
    int? MaxStackCount,
    double? LoadingMeters,
    double? EpqFactor,
    bool IsExchangeable,
    GoodsKind Use);

internal sealed record ProductTemplate(string Name, GoodsKind Kind, ProductAdr? Adr = null);

internal sealed record PackagingTemplate(
    string Name,
    double Quantity,
    double Colli,
    double WeightKg,
    double VolumeM3,
    double? LoadingMeters,
    double? Epq,
    double? LengthCm,
    double? WidthCm,
    double? HeightCm);

internal sealed record TemperatureTemplate(string Name, double? MinCelsius, double? MaxCelsius);

/// <summary>A region with numeric ranges on the leading digits of the postcode, or with a prefix.</summary>
internal sealed record RegionTemplate(string Name, string CountryCode, (int From, int Till)[] Ranges, string? Prefix = null);

/// <summary>
/// The realistic values of the Master Data test dataset (docs/designs/65-tenant-test-datasets.md): the master-data lists
/// a Dutch carrier really keeps, first; beyond that the variants a large tenant builds up over the years (per customer,
/// per brand, per postcode area), so a profile of 2,500 records per type still reads like real data.
/// </summary>
internal static class MasterDataTestCatalog
{
    public static readonly CharacteristicTemplate[] Characteristics =
    {
        new("LAADKLEP", "Laadklep"),
        new("KOEL", "Koelinstallatie"),
        new("DIEPVRIES", "Diepvriesinstallatie"),
        new("ADR", "ADR-uitrusting"),
        new("MEENEEMHEF", "Meeneemheftruck"),
        new("KRAAN", "Laadkraan"),
        new("SCHUIFZEIL", "Schuifzeil"),
        new("MEGA", "Mega-volume (3 m binnenhoogte)"),
        new("DUBBELDEKS", "Dubbeldeks laadvloer"),
        new("GDP", "GDP-gecertificeerd (farma)"),
        new("PALLETWAGEN", "Palletwagen aan boord"),
        new("ZIJBELADING", "Zijbelading mogelijk"),
        new("CONTAINER20", "Containerchassis 20 ft"),
        new("CONTAINER40", "Containerchassis 40 ft"),
        new("KIPPER", "Kipinstallatie"),
        new("TANK", "Tankopbouw"),
        new("SILO", "Silo-opbouw"),
        new("ZEROEMISSIE", "Zero-emissie (ZE-zone)"),
        new("STUURAS", "Gestuurde as"),
        new("SJORMIDDELEN", "Extra sjorbanden en hoekbeschermers"),
        new("TEMPREG", "Temperatuurregistratie"),
        new("CCKARREN", "Geschikt voor CC-containers")
    };

    private static readonly string[] CharacteristicContexts =
    {
        "bij laden", "bij lossen", "bij laden en lossen", "nachtdistributie", "binnenstad", "venstertijd",
        "zaterdaglevering", "spoed"
    };

    public static readonly string[] Places =
    {
        "Amsterdam", "Rotterdam", "Utrecht", "Den Haag", "Eindhoven", "Venlo", "Tilburg", "Zwolle", "Groningen",
        "Antwerpen", "Brussel", "Gent", "Düsseldorf", "Köln", "Duisburg", "Hamburg"
    };

    public static readonly VehicleTypeTemplate[] VehicleTypes =
    {
        new("Trekker 4x2", VehicleTypeKind.Tractor, null, null, 600, 255, 400, 19_000, 7_400, null, 1.05m, null, Array.Empty<string>()),
        new("Trekker 6x2", VehicleTypeKind.Tractor, null, null, 680, 255, 400, 26_000, 8_600, null, 1.15m, null, new[] { "STUURAS" }),
        new("Trekker 4x2 LNG", VehicleTypeKind.Tractor, null, null, 610, 255, 400, 19_000, 7_900, null, 1.10m, FuelType.Lng, Array.Empty<string>()),
        new("Trekker 4x2 elektrisch", VehicleTypeKind.Tractor, null, null, 640, 255, 400, 19_000, 10_200, null, 1.35m, FuelType.Electric, new[] { "ZEROEMISSIE" }),
        new("Bakwagen 12t", VehicleTypeKind.Rigid, 15, 6.2, 800, 255, 350, 12_000, 6_500, 5_000, 0.80m, null, new[] { "LAADKLEP" }),
        new("Bakwagen 18t", VehicleTypeKind.Rigid, 18, 7.2, 1_000, 255, 380, 18_000, 9_000, 8_500, 0.90m, null, new[] { "LAADKLEP" }),
        new("Bakwagen 26t", VehicleTypeKind.Rigid, 20, 8.0, 1_100, 255, 390, 26_000, 12_000, 13_500, 1.00m, null, Array.Empty<string>()),
        new("Koelbakwagen 18t", VehicleTypeKind.Rigid, 16, 7.0, 1_000, 255, 390, 18_000, 9_800, 7_800, 1.00m, null, new[] { "KOEL", "TEMPREG", "LAADKLEP" }),
        new("Bakwagen met kraan", VehicleTypeKind.Rigid, 14, 6.5, 1_000, 255, 380, 26_000, 14_500, 11_000, 1.20m, null, new[] { "KRAAN" }),
        new("Bestelbus 3,5t", VehicleTypeKind.Van, 4, 1.8, 600, 205, 270, 3_500, 2_100, 1_300, 0.42m, null, Array.Empty<string>()),
        new("Koelbus 3,5t", VehicleTypeKind.Van, 3, 1.5, 600, 205, 280, 3_500, 2_400, 1_000, 0.50m, null, new[] { "KOEL" }),
        new("Elektrische bestelbus", VehicleTypeKind.Van, 3, 1.6, 590, 200, 260, 3_500, 2_600, 850, 0.38m, FuelType.Electric, new[] { "ZEROEMISSIE" }),
        new("Schuifzeiltrailer", VehicleTypeKind.Trailer, 33, 13.6, 1_360, 255, 400, 39_000, 6_800, 32_000, 0.20m, FuelType.NotApplicable, new[] { "SCHUIFZEIL", "ZIJBELADING" }),
        new("Megatrailer", VehicleTypeKind.Trailer, 33, 13.6, 1_360, 255, 400, 39_000, 7_100, 31_500, 0.22m, FuelType.NotApplicable, new[] { "MEGA", "SCHUIFZEIL" }),
        new("Koeltrailer", VehicleTypeKind.Trailer, 33, 13.3, 1_340, 260, 400, 39_000, 9_000, 29_500, 0.30m, FuelType.NotApplicable, new[] { "KOEL", "TEMPREG" }),
        new("Diepvriestrailer", VehicleTypeKind.Trailer, 33, 13.3, 1_340, 260, 400, 39_000, 9_300, 29_000, 0.32m, FuelType.NotApplicable, new[] { "DIEPVRIES", "TEMPREG" }),
        new("Dubbeldeks koeltrailer", VehicleTypeKind.Trailer, 66, 13.3, 1_340, 260, 400, 39_000, 10_200, 28_000, 0.36m, FuelType.NotApplicable, new[] { "KOEL", "DUBBELDEKS", "TEMPREG" }),
        new("Containerchassis 40 ft", VehicleTypeKind.Trailer, 25, 12.2, 1_230, 250, 150, 39_000, 4_600, 34_000, 0.18m, FuelType.NotApplicable, new[] { "CONTAINER40" }),
        new("Containerchassis 20 ft", VehicleTypeKind.Trailer, 11, 6.1, 730, 250, 150, 39_000, 3_900, 34_500, 0.16m, FuelType.NotApplicable, new[] { "CONTAINER20" }),
        new("Tankoplegger", VehicleTypeKind.Trailer, null, null, 1_250, 255, 380, 39_000, 7_500, 31_000, 0.28m, FuelType.NotApplicable, new[] { "TANK", "ADR" }),
        new("Kipoplegger", VehicleTypeKind.Trailer, null, null, 1_000, 255, 360, 39_000, 6_900, 32_000, 0.25m, FuelType.NotApplicable, new[] { "KIPPER" }),
        new("Dieplader", VehicleTypeKind.Trailer, null, 13.6, 1_400, 255, 90, 48_000, 11_000, 37_000, 0.30m, FuelType.NotApplicable, new[] { "SJORMIDDELEN" }),
        new("Aanhangwagen (wipkar)", VehicleTypeKind.Trailer, 18, 7.6, 780, 255, 300, 18_000, 4_200, 13_800, 0.18m, FuelType.NotApplicable, new[] { "SCHUIFZEIL" })
    };

    private static readonly string[] TruckBrands = { "DAF", "Scania", "Volvo", "Mercedes-Benz", "MAN", "Iveco", "Renault" };
    private static readonly string[] VanBrands = { "Mercedes-Benz", "Ford", "Volkswagen", "Renault", "Iveco", "Fiat", "Peugeot" };
    private static readonly string[] TrailerBrands = { "Krone", "Schmitz Cargobull", "Kögel", "Pacton", "Van Hool", "Burg", "Nooteboom" };

    /// <summary>The world manufacturer identifiers (first three VIN characters) per brand.</summary>
    public static readonly Dictionary<string, string> Wmi = new(StringComparer.Ordinal)
    {
        ["DAF"] = "XLR", ["Scania"] = "YS2", ["Volvo"] = "YV2", ["Mercedes-Benz"] = "WDB", ["MAN"] = "WMA",
        ["Iveco"] = "ZCF", ["Renault"] = "VF6", ["Ford"] = "WF0", ["Volkswagen"] = "WV1", ["Fiat"] = "ZFA",
        ["Peugeot"] = "VF3", ["Krone"] = "WKE", ["Schmitz Cargobull"] = "WSM", ["Kögel"] = "WK0", ["Pacton"] = "XLX",
        ["Van Hool"] = "YE2", ["Burg"] = "XLB", ["Nooteboom"] = "XL9"
    };

    public static string[] BrandsOf(VehicleTypeKind kind) => kind switch
    {
        VehicleTypeKind.Trailer => TrailerBrands,
        VehicleTypeKind.Van => VanBrands,
        _ => TruckBrands
    };

    public static readonly TransportUnitTemplate[] TransportUnits =
    {
        new("Europallet", 120, 80, 14.4, 25, 1_500, true, 3, 0.4, 1.0, true, GoodsKind.General),
        new("Blokpallet", 120, 100, 14.4, 30, 1_500, true, 3, 0.5, 1.25, true, GoodsKind.General),
        new("Wegwerppallet", 120, 80, 12, 15, 800, true, 2, 0.4, 1.0, false, GoodsKind.General),
        new("Halve pallet (Düsseldorfer)", 80, 60, 15, 10, 500, true, 3, 0.2, 0.5, true, GoodsKind.General),
        new("Kwartpallet", 60, 40, 15, 5, 250, true, 3, 0.1, 0.25, false, GoodsKind.General),
        new("Rolcontainer", 72, 80, 170, 35, 500, false, null, 0.24, 0.6, true, GoodsKind.Food),
        new("CC-container (Deense kar)", 135, 56.5, 190, 40, 300, false, null, 0.28, 0.8, true, GoodsKind.Food),
        new("Gitterbox", 124, 83.5, 97, 70, 1_500, true, 5, 0.4, 1.0, true, GoodsKind.General),
        new("IBC 1.000 l", 120, 100, 116, 60, 1_000, true, 2, 0.5, 1.25, false, GoodsKind.Chemical),
        new("Big bag", 90, 90, 110, 2, 1_000, false, null, 0.35, 1.0, false, GoodsKind.Chemical),
        new("Vat 200 l", 58, 58, 88, 18, 200, true, 2, 0.15, 0.35, false, GoodsKind.Chemical),
        new("Krat (fust)", 40, 30, 30, 1.5, 25, true, 8, null, 0.05, true, GoodsKind.Food),
        new("Doos (colli)", 60, 40, 40, 0.5, 30, true, 6, null, 0.05, false, GoodsKind.General),
        new("Pakket", 40, 30, 20, 0.2, 31.5, true, 10, null, 0.02, false, GoodsKind.General),
        new("Container 20 ft", 606, 244, 259, 2_200, 28_000, false, null, 6.1, 11, false, GoodsKind.General),
        new("Container 40 ft", 1_219, 244, 259, 3_700, 26_500, false, null, 12.2, 25, false, GoodsKind.General),
        new("Langgoed (bundel)", 600, 50, 50, 0, 2_000, true, 3, 6.0, 3.0, false, GoodsKind.General),
        new("Kabelhaspel", 160, 100, 160, 120, 2_500, false, null, 1.0, 1.5, true, GoodsKind.General),
        new("Bloemenemmer (Procona)", 40, 30, 35, 0.6, 15, true, 6, null, 0.03, true, GoodsKind.Food)
    };

    private static readonly string[] TransportUnitPools = { "EPAL", "CHEP", "LPR", "IPP", "klanteigen", "gehuurd", "eigen" };

    public static readonly TemperatureTemplate[] Temperatures =
    {
        new("Ambient 15–25 °C", 15, 25),
        new("Gekoeld 2–8 °C", 2, 8),
        new("Gekoeld 0–4 °C", 0, 4),
        new("Diepvries -18 °C", -25, -18),
        new("Diepvries -25 °C", -30, -25),
        new("Geconditioneerd 12–18 °C", 12, 18),
        new("Bloemen 2–6 °C", 2, 6),
        new("Farma 15–25 °C", 15, 25),
        new("Farma 2–8 °C", 2, 8),
        new("Chocolade 12–18 °C", 12, 18),
        new("Vorstvrij (boven 2 °C)", 2, null),
        new("Bananen 13–14 °C", 13, 14),
        new("Vers vlees 0–2 °C", 0, 2),
        new("IJs -22 °C", -28, -22)
    };

    public static readonly string[] TemperatureGroups =
    {
        "zuivel", "vlees", "vis", "AGF", "bloemen en planten", "farma", "bakkerij", "ijs", "chocolade", "dranken", "kaas", "eieren"
    };

    public static readonly ProductTemplate[] Products =
    {
        new("Zuivelproducten", GoodsKind.Food), new("Kaas", GoodsKind.Food), new("Vleeswaren", GoodsKind.Food),
        new("Vers vlees", GoodsKind.Food), new("Vis en schaaldieren", GoodsKind.Food), new("Diepvriesgroenten", GoodsKind.Food),
        new("Groenten en fruit (AGF)", GoodsKind.Food), new("Aardappelen", GoodsKind.Food), new("Uien", GoodsKind.Food),
        new("Eieren", GoodsKind.Food), new("Brood en banket", GoodsKind.Food), new("Chocolade en zoetwaren", GoodsKind.Food),
        new("IJs", GoodsKind.Food), new("Frisdrank", GoodsKind.Food), new("Bier", GoodsKind.Food), new("Wijn", GoodsKind.Food),
        new("Mineraalwater", GoodsKind.Food), new("Diervoeders", GoodsKind.Food), new("Snijbloemen", GoodsKind.Food),
        new("Potplanten", GoodsKind.Food), new("Bloembollen", GoodsKind.Food), new("Bouwmaterialen", GoodsKind.General),
        new("Isolatiemateriaal", GoodsKind.General), new("Dakpannen", GoodsKind.General), new("Tegels", GoodsKind.General),
        new("Staalprofielen", GoodsKind.General), new("Hout en plaatmateriaal", GoodsKind.General), new("Glas", GoodsKind.General),
        new("Kunststof granulaat", GoodsKind.General), new("Papierrollen", GoodsKind.General), new("Kartonnen dozen", GoodsKind.General),
        new("Meubels", GoodsKind.General), new("Witgoed", GoodsKind.General), new("Consumentenelektronica", GoodsKind.General),
        new("Textiel", GoodsKind.General), new("Machineonderdelen", GoodsKind.General), new("Auto-onderdelen", GoodsKind.General),
        new("Banden", GoodsKind.General), new("Cosmetica", GoodsKind.General), new("Farmaceutische producten", GoodsKind.General),
        new("Medische hulpmiddelen", GoodsKind.General), new("Lege emballage", GoodsKind.General), new("Gemengde stukgoederen", GoodsKind.General),
        new("E-commerce zendingen", GoodsKind.General),
        new("Dieselolie (UN 1202)", GoodsKind.Chemical, new ProductAdr("1202", "3", "III", "DIESELBRANDSTOF", null, "F1", "3", "30", "(D/E)", 3, true, false, false, 56, "363 640L 664")),
        new("Benzine (UN 1203)", GoodsKind.Chemical, new ProductAdr("1203", "3", "II", "BENZINE", null, "F1", "3", "33", "(D/E)", 2, true, false, false, -40, "243 534 664")),
        new("Brandbare vloeistof (UN 1993)", GoodsKind.Chemical, new ProductAdr("1993", "3", "III", "BRANDBARE VLOEISTOF, N.E.G.", "xyleen", "F1", "3", "30", "(D/E)", 3, false, false, false, 40, "274 601 640E")),
        new("Spuitbussen (UN 1950)", GoodsKind.Chemical, new ProductAdr("1950", "2", null, "SPUITBUSSEN", null, "5F", "2.1", null, "(D)", 2, false, true, false, null, "190 327 344 625")),
        new("Zoutzuur (UN 1789)", GoodsKind.Chemical, new ProductAdr("1789", "8", "II", "ZOUTZUUR", null, "C1", "8", "80", "(E)", 2)),
        new("Natronloog (UN 1824)", GoodsKind.Chemical, new ProductAdr("1824", "8", "II", "NATRIUMHYDROXIDEOPLOSSING", null, "C5", "8", "80", "(E)", 2)),
        new("Zwavelzuur (UN 1830)", GoodsKind.Chemical, new ProductAdr("1830", "8", "II", "ZWAVELZUUR", null, "C1", "8", "80", "(E)", 2)),
        new("Milieugevaarlijke vloeistof (UN 3082)", GoodsKind.Chemical, new ProductAdr("3082", "9", "III", "MILIEUGEVAARLIJKE STOF, VLOEIBAAR, N.E.G.", "koperverbindingen", "M6", "9", "90", "(-)", 3, true, false, false, null, "274 335 375 601")),
        new("Milieugevaarlijke vaste stof (UN 3077)", GoodsKind.Chemical, new ProductAdr("3077", "9", "III", "MILIEUGEVAARLIJKE STOF, VAST, N.E.G.", "zinkoxide", "M7", "9", "90", "(-)", 3, true, false, false, null, "274 335 375 601")),
        new("LPG (UN 1075)", GoodsKind.Chemical, new ProductAdr("1075", "2", null, "PETROLEUMGASSEN, VLOEIBAAR GEMAAKT", null, "2F", "2.1", "23", "(B/D)", 2, false, false, false, null, "274 583 639 662")),
        new("Ammoniak (UN 1005)", GoodsKind.Chemical, new ProductAdr("1005", "2", null, "AMMONIAK, WATERVRIJ", null, "2TC", "2.3+8", "268", "(C/D)", 1, true, false, false, null, "23 379")),
        new("Ethanol (UN 1170)", GoodsKind.Chemical, new ProductAdr("1170", "3", "II", "ETHANOL", null, "F1", "3", "33", "(D/E)", 2, false, false, false, 13, "144 601")),
        new("Aceton (UN 1090)", GoodsKind.Chemical, new ProductAdr("1090", "3", "II", "ACETON", null, "F1", "3", "33", "(D/E)", 2, false, false, false, -20, null)),
        new("Waterstofperoxide (UN 2014)", GoodsKind.Chemical, new ProductAdr("2014", "5.1", "II", "WATERSTOFPEROXIDE, OPLOSSING IN WATER", null, "OC1", "5.1+8", "58", "(E)", 2)),
        new("Lithium-ionbatterijen (UN 3480)", GoodsKind.Chemical, new ProductAdr("3480", "9", null, "LITHIUMIONBATTERIJEN", null, "M4", "9A", null, "(E)", 2, false, false, false, null, "188 230 310 348 376 377 387 636")),
        new("Verf (UN 1263)", GoodsKind.Chemical, new ProductAdr("1263", "3", "III", "VERF", null, "F1", "3", "30", "(D/E)", 3, false, false, false, 35, "163 367 640E 650")),
        new("Accu's, nat (UN 2794)", GoodsKind.Chemical, new ProductAdr("2794", "8", null, "ACCUMULATOREN, NAT, GEVULD MET ZUUR", null, "C11", "8", null, "(E)", 3, false, false, false, null, "295 598"))
    };

    public static readonly PackagingTemplate[] Packagings =
    {
        new("op europallet", 1, 40, 650, 1.44, 0.4, 1.0, 120, 80, 150),
        new("op blokpallet", 1, 48, 800, 1.80, 0.5, 1.25, 120, 100, 150),
        new("in rolcontainers", 1, 30, 250, 0.98, 0.24, 0.6, 72, 80, 170),
        new("in CC-containers", 1, 24, 180, 1.45, 0.28, 0.8, 135, 56.5, 190),
        new("in kratten", 20, 20, 300, 0.72, null, 0.5, 40, 30, 30),
        new("los gestapeld (colli)", 30, 30, 360, 2.9, null, 1.0, 60, 40, 40),
        new("in big bags", 1, 1, 1_000, 0.89, 0.35, 1.0, 90, 90, 110),
        new("in IBC's", 1, 1, 1_100, 1.39, 0.5, 1.25, 120, 100, 116),
        new("in vaten", 4, 4, 880, 1.2, 0.4, 1.0, 120, 80, 100)
    };

    private static readonly (string Name, double Factor)[] WeightClasses =
    {
        ("tot 500 kg", 0.5), ("500–1.000 kg", 1.0), ("1–5 ton", 3.0), ("komplete lading", 20.0)
    };

    public static readonly string[] Sectors =
    {
        "Groothandel", "Retail", "Food", "Zuivel", "AGF", "Bloemen en planten", "Bouw", "Bouwmaterialen", "Metaal",
        "Chemie", "Farma", "Automotive", "E-commerce", "Elektronica", "Meubels", "Papier en karton", "Kunststoffen",
        "Dranken", "Agro", "Textiel", "Machinebouw", "Installatietechniek", "Verpakkingen", "Diervoeders", "Recycling",
        "Evenementen", "Overheid", "Zorg", "Horeca", "Expediteurs"
    };

    private static readonly string[] Segments =
    {
        "Key account", "A-klant", "B-klant", "C-klant", "Spot", "Contract", "Export", "Import", "Nachtdistributie", "Stukgoed"
    };

    private static readonly string[] SalesRegions =
    {
        "Noord", "Oost", "Zuid", "West", "Midden", "Randstad", "Brabant", "Limburg", "Vlaanderen", "Wallonië", "Brussel",
        "NRW", "Niedersachsen", "Hamburg"
    };

    /// <summary>Named planning regions with approximate real postcode ranges (4-digit NL/BE, 5-digit DE).</summary>
    public static readonly RegionTemplate[] NamedRegions =
    {
        new("Groot-Amsterdam", "NL", new[] { (1000, 1199) }),
        new("Noord-Holland Noord", "NL", new[] { (1400, 1899) }),
        new("Haarlem en Kennemerland", "NL", new[] { (1900, 2199) }),
        new("Zuid-Holland", "NL", new[] { (2200, 3399) }),
        new("Rijnmond", "NL", new[] { (3000, 3299) }),
        new("Utrecht", "NL", new[] { (3400, 3999) }),
        new("Zeeland", "NL", new[] { (4300, 4599) }),
        new("West-Brabant", "NL", new[] { (4600, 4999) }),
        new("Midden- en Oost-Brabant", "NL", new[] { (5000, 5799) }),
        new("Limburg (NL)", "NL", new[] { (5800, 6499) }),
        new("Gelderland", "NL", new[] { (6500, 7399) }),
        new("Overijssel", "NL", new[] { (7400, 7799), (8000, 8299) }),
        new("Drenthe", "NL", new[] { (7800, 7999), (9400, 9499) }),
        new("Flevoland", "NL", new[] { (1300, 1399), (8200, 8299) }),
        new("Friesland", "NL", new[] { (8400, 9299) }),
        new("Groningen", "NL", new[] { (9300, 9399), (9500, 9999) }),
        new("Brussels Gewest", "BE", new[] { (1000, 1299) }),
        new("Antwerpen (provincie)", "BE", new[] { (2000, 2999) }),
        new("Vlaams-Brabant", "BE", new[] { (1500, 1999), (3000, 3499) }),
        new("Limburg (BE)", "BE", new[] { (3500, 3999) }),
        new("Luik", "BE", new[] { (4000, 4999) }),
        new("Henegouwen", "BE", new[] { (6000, 6599), (7000, 7999) }),
        new("West-Vlaanderen", "BE", new[] { (8000, 8999) }),
        new("Oost-Vlaanderen", "BE", new[] { (9000, 9999) }),
        new("Ruhrgebiet", "DE", new[] { (44000, 47999) }),
        new("Rheinland", "DE", new[] { (40000, 42999), (50000, 53999) }),
        new("Münsterland", "DE", new[] { (48000, 48999) }),
        new("Niedersachsen West", "DE", new[] { (26000, 27999), (49000, 49999) }),
        new("Hamburg", "DE", new[] { (20000, 22999) }),
        new("Berlin", "DE", new[] { (10000, 14999) }),
        new("Rhein-Main", "DE", new[] { (60000, 65999) }),
        new("Stuttgart", "DE", new[] { (70000, 73999) }),
        new("München", "DE", new[] { (80000, 85999) })
    };

    public static readonly string[] CarrierWords = { "Transport", "Transport", "Logistiek", "Koeltransport", "Expeditie", "Trucking", "Distributie" };
    public static readonly string[] CarrierWordsDe = { "Spedition", "Transporte", "Logistik", "Kühltransporte" };
    public static readonly string[] SupplierWords = { "Truckservice", "Bandenservice", "Tankstations", "Truckwash", "Lease", "Garagebedrijf", "Brandstoffen", "Trailerverhuur", "Emballage", "Bedrijfskleding" };
    public static readonly string[] SupplierWordsDe = { "Nutzfahrzeuge", "Reifenservice", "Tankstellen", "Fahrzeugpflege", "Leasing", "Werkstatt" };
    public static readonly string[] NlForms = { "B.V.", "B.V.", "B.V.", "V.O.F." };
    public static readonly string[] BeForms = { "BV", "BV", "NV", "SRL" };
    public static readonly string[] DeForms = { "GmbH", "GmbH", "GmbH & Co. KG", "KG" };

    public static readonly string[] RelationRemarks =
    {
        "Laden alleen op afspraak.", "Lossen via dock 4, melden bij de portier.", "Geen levering op vrijdagmiddag.",
        "Venstertijd 06:00–10:00.", "Pallets ruilen verplicht.", "Bij aankomst bellen naar de expeditie.",
        "Terrein niet toegankelijk voor LZV.", "Vooraanmelding 24 uur vooraf via het portaal.", "Afleveren tot 16:00.",
        "Laadklep vereist, geen dock aanwezig."
    };

    public static readonly (string Title, string? Department)[] PlanningTitles =
    {
        ("Planner", "Planning"), ("Logistiek medewerker", "Logistiek"), ("Expeditiemedewerker", "Expeditie"),
        ("Hoofd logistiek", "Logistiek"), ("Customer service medewerker", "Klantenservice")
    };

    public static readonly (string Title, string? Department)[] InvoicingTitles =
    {
        ("Crediteurenadministratie", "Financiën"), ("Financieel medewerker", "Financiën"), ("Controller", "Financiën")
    };

    public static readonly (string Title, string? Department)[] SalesTitles =
    {
        ("Inkoper", "Inkoop"), ("Accountmanager", "Verkoop"), ("Commercieel manager", "Verkoop")
    };

    public static readonly (string Title, string? Department)[] ManagementTitles =
    {
        ("Directeur", null), ("Operationeel manager", "Operations"), ("Vestigingsmanager", null)
    };

    /// <summary>BIC and bank name for the Dutch bank codes TestNames draws from.</summary>
    public static readonly Dictionary<string, (string Bic, string Name)> NlBanks = new(StringComparer.Ordinal)
    {
        ["INGB"] = ("INGBNL2A", "ING Bank"), ["RABO"] = ("RABONL2U", "Rabobank"), ["ABNA"] = ("ABNANL2A", "ABN AMRO"),
        ["TRIO"] = ("TRIONL2U", "Triodos Bank"), ["SNSB"] = ("SNSBNL2A", "SNS"), ["KNAB"] = ("KNABNL2H", "Knab"),
        ["BUNQ"] = ("BUNQNL2A", "bunq")
    };

    public static readonly string[] FleetSystems = { "Transics", "Webfleet", "Mix Telematics", "Fleetboard" };

    public static IEnumerable<(string Name, (CharacteristicTemplate Base, string Code))> CharacteristicCandidates()
    {
        foreach (var c in Characteristics)
        {
            yield return (c.Name, (c, c.Code));
        }

        for (var i = 0; i < CharacteristicContexts.Length; i++)
        {
            foreach (var c in Characteristics)
            {
                yield return ($"{c.Name} – {CharacteristicContexts[i]}", (c, $"{c.Code}-{i + 1:00}"));
            }
        }

        for (var p = 0; p < Places.Length; p++)
        {
            for (var i = 0; i < CharacteristicContexts.Length; i++)
            {
                foreach (var c in Characteristics)
                {
                    yield return ($"{c.Name} – {CharacteristicContexts[i]} – {Places[p]}", (c, $"{c.Code}-{i + 1:00}-{p + 1:00}"));
                }
            }
        }
    }

    public static IEnumerable<(string Name, (VehicleTypeTemplate Template, string? Brand))> VehicleTypeCandidates()
    {
        foreach (var t in VehicleTypes)
        {
            yield return (t.Name, (t, null));
        }

        foreach (var t in VehicleTypes)
        {
            foreach (var brand in BrandsOf(t.Kind))
            {
                yield return ($"{t.Name} {brand}", (t, brand));
            }
        }

        foreach (var variant in new[] { "standaard", "zware uitvoering" })
        {
            for (var year = 2026; year >= 2014; year--)
            {
                foreach (var t in VehicleTypes)
                {
                    foreach (var brand in BrandsOf(t.Kind))
                    {
                        yield return ($"{t.Name} {brand} {year} ({variant})", (t, brand));
                    }
                }
            }
        }
    }

    public static IEnumerable<(string Name, TransportUnitTemplate Template)> TransportUnitCandidates()
    {
        foreach (var t in TransportUnits)
        {
            yield return (t.Name, t);
        }

        foreach (var pool in TransportUnitPools)
        {
            foreach (var t in TransportUnits)
            {
                yield return ($"{t.Name} {pool}", t);
            }
        }

        foreach (var stacking in new[] { "stapelbaar", "niet stapelbaar" })
        {
            for (var height = 60; height <= 240; height += 20)
            {
                foreach (var pool in TransportUnitPools)
                {
                    foreach (var t in TransportUnits)
                    {
                        yield return ($"{t.Name} {pool} hoogte {height / 100.0:0.0#} m {stacking}", t with { HeightCm = t.HeightCm < 50 ? t.HeightCm : height, IsStackable = stacking == "stapelbaar" && t.IsStackable });
                    }
                }
            }
        }
    }

    public static IEnumerable<(string Name, TemperatureTemplate Template)> TemperatureCandidates()
    {
        foreach (var t in Temperatures)
        {
            yield return (t.Name, t);
        }

        var ranges = new List<TemperatureTemplate>();
        for (var min = -30; min <= 20; min++)
        {
            for (var width = 2; width <= 10; width++)
            {
                var max = min + width;
                var range = min < 0 ? $"{Celsius(min)} tot {Celsius(max)}" : $"{Celsius(min)}–{Celsius(max)}";
                ranges.Add(new TemperatureTemplate($"{TemperatureLabel(max)} {range} °C", min, max));
            }
        }

        foreach (var t in ranges)
        {
            yield return (t.Name, t);
        }

        foreach (var group in TemperatureGroups)
        {
            foreach (var t in ranges)
            {
                yield return ($"{t.Name} – {group}", t);
            }
        }
    }

    public static IEnumerable<(string Name, (ProductTemplate Template, PackagingTemplate Packaging, double WeightFactor))> ProductCandidates()
    {
        /* Two ordinary products, then a dangerous one: a small dataset holds ADR products too. */
        var pallet = Packagings[0];
        var ordinary = Products.Where(p => p.Adr == null).ToList();
        var adr = Products.Where(p => p.Adr != null).ToList();
        for (int o = 0, a = 0; o < ordinary.Count || a < adr.Count;)
        {
            for (var i = 0; i < 2 && o < ordinary.Count; i++, o++)
            {
                yield return (ordinary[o].Name, (ordinary[o], pallet, 1.0));
            }

            if (a < adr.Count)
            {
                yield return (adr[a].Name, (adr[a], pallet, 1.0));
                a++;
            }
        }

        foreach (var packaging in Packagings)
        {
            foreach (var p in Products)
            {
                yield return ($"{p.Name} {packaging.Name}", (p, packaging, 1.0));
            }
        }

        foreach (var (weightClass, factor) in WeightClasses)
        {
            foreach (var packaging in Packagings)
            {
                foreach (var p in Products)
                {
                    yield return ($"{p.Name} {packaging.Name}, {weightClass}", (p, packaging, factor));
                }
            }
        }
    }

    public static IEnumerable<(string Name, bool Unused)> RelationCategoryCandidates()
    {
        foreach (var sector in Sectors)
        {
            yield return (sector, false);
        }

        foreach (var segment in Segments)
        {
            foreach (var sector in Sectors)
            {
                yield return ($"{sector} – {segment}", false);
            }
        }

        foreach (var region in SalesRegions)
        {
            foreach (var segment in Segments)
            {
                foreach (var sector in Sectors)
                {
                    yield return ($"{sector} – {segment} – {region}", false);
                }
            }
        }
    }

    public static IEnumerable<(string Name, RegionTemplate Template)> RegionCandidates()
    {
        foreach (var r in NamedRegions)
        {
            yield return (r.Name, r);
        }

        for (var d = 10; d <= 99; d++)
        {
            yield return ($"Postcodegebied NL {d}xx", new RegionTemplate(string.Empty, "NL", new[] { (d * 100, d * 100 + 99) }));
        }

        for (var d = 10; d <= 99; d++)
        {
            yield return ($"Postcodegebied BE {d}xx", new RegionTemplate(string.Empty, "BE", new[] { (d * 100, d * 100 + 99) }));
        }

        for (var d = 1; d <= 99; d++)
        {
            yield return ($"PLZ-Bereich {d:00}xxx", new RegionTemplate(string.Empty, "DE", new[] { (d * 1000, d * 1000 + 999) }));
        }

        for (var d = 100; d <= 999; d++)
        {
            yield return ($"Postcodegebied NL {d}x", new RegionTemplate(string.Empty, "NL", Array.Empty<(int, int)>(), d.ToString(CultureInfo.InvariantCulture)));
        }

        for (var d = 10; d <= 999; d++)
        {
            yield return ($"PLZ-Bereich {d:000}xx", new RegionTemplate(string.Empty, "DE", Array.Empty<(int, int)>(), d.ToString("000", CultureInfo.InvariantCulture)));
        }

        for (var d = 100; d <= 999; d++)
        {
            yield return ($"Postcodegebied BE {d}x", new RegionTemplate(string.Empty, "BE", new[] { (d * 10, d * 10 + 9) }));
        }
    }

    /// <summary>
    /// The first <paramref name="count"/> distinct names (case-insensitive, as the unique indexes compare) of the
    /// candidates, cut to <paramref name="maxLength"/>. When the candidates run out the names are numbered "(2)", "(3)", …
    /// as a user does when a name is taken.
    /// </summary>
    public static List<(string Name, T Item)> TakeUnique<T>(IEnumerable<(string Name, T Item)> candidates, int count, int maxLength)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<(string Name, T Item)>(count);
        foreach (var (candidate, item) in candidates)
        {
            if (result.Count == count)
            {
                return result;
            }

            var name = Fit(candidate, maxLength);
            if (seen.Add(name))
            {
                result.Add((name, item));
            }
        }

        var distinct = result.Count;
        for (var serial = 2; result.Count < count && distinct > 0; serial++)
        {
            for (var i = 0; i < distinct && result.Count < count; i++)
            {
                var suffix = $" ({serial})";
                var numbered = Fit(result[i].Name, maxLength - suffix.Length) + suffix;
                if (seen.Add(numbered))
                {
                    result.Add((numbered, result[i].Item));
                }
            }
        }

        return result;
    }

    public static string Fit(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength].TrimEnd();

    /// <summary>A GLN (GS1 location number): 13 digits with the GS1 mod-10 check digit.</summary>
    public static string Gln(long serial)
        => WithGs1CheckDigit("87" + (serial % 10_000_000_000L).ToString("0000000000", CultureInfo.InvariantCulture));

    /// <summary>Appends the GS1 mod-10 check digit (GLN, EAN-13) to a 12-digit body.</summary>
    public static string WithGs1CheckDigit(string body)
    {
        var sum = 0;
        for (var i = 0; i < body.Length; i++)
        {
            var digit = body[body.Length - 1 - i] - '0';
            sum += i % 2 == 0 ? digit * 3 : digit;
        }

        return body + ((10 - sum % 10) % 10).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>A VIN of the ISO 3779 shape for the brand: its WMI and 14 random characters without I, O and Q.</summary>
    public static string Vin(Random random, string brand)
    {
        const string chars = "ABCDEFGHJKLMNPRSTUVWXYZ0123456789";
        var wmi = Wmi.TryGetValue(brand, out var w) ? w : "XLR";
        var tail = new char[14];
        for (var i = 0; i < tail.Length; i++)
        {
            tail[i] = chars[random.Next(chars.Length)];
        }

        return wmi + new string(tail);
    }

    /// <summary>"de Jong" → ("de", "Jong"): the Dutch prefix goes into the middle name, as the person forms keep it.</summary>
    public static (string? Middle, string Last) SplitSurname(string surname)
    {
        var parts = surname.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var prefix = parts.TakeWhile(p => char.IsLower(p[0])).Count();
        return prefix == 0 || prefix == parts.Length
            ? (null, surname)
            : (string.Join(' ', parts.Take(prefix)), string.Join(' ', parts.Skip(prefix)));
    }

    public static string Capitalize(string value) => value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private static string TemperatureLabel(int max) => max switch
    {
        <= -15 => "Diepvries",
        <= 8 => "Gekoeld",
        <= 18 => "Geconditioneerd",
        _ => "Ambient"
    };

    private static string Celsius(int value) => value.ToString(CultureInfo.InvariantCulture);
}
