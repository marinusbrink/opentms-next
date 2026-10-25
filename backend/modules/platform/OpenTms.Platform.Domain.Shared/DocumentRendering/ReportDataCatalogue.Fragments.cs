using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenTms.Platform.ReportDefinitions;

namespace OpenTms.Platform.DocumentRendering;

public static partial class ReportDataCatalogue
{
    /// <summary>
    /// The OpenTMS payload classes (legacy <c>PdfReportDefinition/Dtos/Request</c>), each defined once and composed per
    /// purpose. Default sample values live on the members; <see cref="SampleSet"/> personas vary them where one class
    /// appears in several roles (the own company, the customer, a loading address, a driver). Fields are declared in
    /// dependency order: a static field may only use fields above it.
    /// </summary>
    private static class Fragments
    {
        // ------------------------------------------------------------------ sample constants

        private const string OrderId = "3b8f2a61-7c4d-4e95-a1d2-6f0e9b8c7a51";
        private const string OrderNumber = "2026-01234";
        private const string OrderReference = "PO-778812";
        private const string InvoiceId = "8e4c1f27-5a9b-4d36-b0e8-2c7f1a9d6b34";
        private const string FileId = "c51d9e80-2b6a-4f17-8d43-9a0e7b2c1f68";
        private const string FileNumber = "R2026-0421";
        private const string PurchaseOrderId = "4a7e2c19-8d3f-4b60-9e15-0c6d8f2a7b93";
        private const string PeriodRelationNote = "the customer whose balance this is";

        private const string CompanyLogo =
            "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

        private static readonly string[] CustomPropertyNames =
        [
            "Klantreferentie", "Afdeling", "Kostenplaats", "Project", "Laadnummer",
            "Containerreferentie", "Zegelnummer", "Douanestatus", "Prioriteit", "Extern commentaar"
        ];

        private static readonly string[] CustomPropertyValues =
        [
            "KR-55120", "Inkoop vers", "KP-310", "Najaarsactie 2026", "LN-88231",
            "MSKU1234565", "ZG-004512", "T1", "Hoog", "Bellen bij aankomst"
        ];

        private static readonly string[] SecondCustomPropertyValues =
        [
            "KR-55187", "Inkoop droog", "KP-320", "Winteractie 2026", "LN-88240",
            "MSKU7654321", "ZG-004519", "T2", "Normaal", "Vooraf melden"
        ];

        // ------------------------------------------------------------------ Country, RelationAddress, Relation

        private static readonly CatalogueFragment Country = F("Country",
            Str("Name", "Country name in the tenant's language.", "Nederland"),
            Str("Iso3166_2", "ISO 3166-1 alpha-2 code.", "NL"),
            Str("Iso3166_3", "ISO 3166-1 alpha-3 code.", "NLD"),
            Int("Iso3166_Number", "ISO 3166-1 numeric code.", 528));

        private static readonly CatalogueFragment RelationAddress = F("RelationAddress",
            Bool("IsPostalAddress", "The address type is the postal address.", false),
            Bool("IsVisitingAddress", "The address type is the visiting address.", true),
            Obj("Country", Country, "Country of the address."),
            Str("PostalCode", "Postal code as entered.", "2991 XJ"),
            Str("City", "City.", "Barendrecht"),
            Str("AddressLine1", "Street (or \"Postbus\").", "Dierensteinweg"),
            Str("AddressLine2", "Second address line.", "Unit 3"),
            Int("Number", "House number.", 30),
            Str("NumberSuffix", "House number suffix.", "A"),
            Dec("Longitude", "Longitude (WGS84).", 4.5328),
            Dec("Latitude", "Latitude (WGS84).", 51.8576),
            Str("TimeZone", "Windows time zone id of the address.", "W. Europe Standard Time"),
            Str("Kix", "PostNL KIX code computed by OpenTMS: NL postal code + house number (+ \"X\" + numeric suffix); "
                + "abroad ISO2 country code + postal code.", null).From(Kix));

        private static readonly CatalogueFragment Relation = F("Relation",
            Id("Id", "Id of the relation.", "6d2b7e1a-3c4f-4a8e-9b71-2f5e8c0d4a11"),
            Str("Name", "Name.", "Klant Foods B.V."),
            Str("FullName", "Full (legal) name.", "Klant Foods B.V."),
            Str("SearchName", "Search name.", "KLANTFOODS"),
            Str("ChamberOfCommerceNo", "Chamber of Commerce (KvK) number.", "24412345"),
            Str("TelephoneNumber", "Telephone number.", "+31 180 612 345"),
            Str("MobileNumber", "Mobile number.", "+31 6 12345678"),
            Str("FaxNumber", "Fax number.", "+31 180 612 399"),
            Str("EmergencyNumber", "Emergency number.", "+31 6 87654321"),
            Str("EMailAddress", "E-mail address.", "info@klantfoods.nl"),
            Str("Url", "Website.", "https://www.klantfoods.nl"),
            Str("InstantMessagerAddress", "Instant messenger address.", "klantfoods-planning"),
            Str("SkypeAddress", "Skype address.", "klantfoods.planning"),
            Str("VatNo", "VAT number.", "NL812345678B01"),
            Str("Iban", "IBAN.", "NL91ABNA0417164300"),
            Str("BankName", "Bank name.", "ABN AMRO"),
            Str("Bic", "BIC.", "ABNANL2A"),
            Str("EMailAddressForInvoices", "E-mail address for invoices.", "facturen@klantfoods.nl"),
            Int("PaymentTerm", "Default payment term in days.", 30),
            Str("CarrierCode", "Carrier code.", "KLF"),
            Bool("DoorKeyInPossession", "The company holds a door key of this location.", false),
            Str("LocationSecurityAccessCode", "Access code of the location.", "4711#"),
            Str("DebtorNumber", "Debtor number in the accounting system.", "D10045"),
            Str("PreferredLanguage", "Preferred language (culture name).", "nl"),
            Str("InvoiceTo", "Invoice addressee line.", "Klant Foods B.V. t.a.v. crediteurenadministratie"),
            Str("InternalInvoicingRemarks", "Internal invoicing remarks.", "PO-nummer verplicht op de factuur"),
            Str("InternalRemarks", "Internal remarks.", "Laden alleen na telefonische afspraak"),
            List("Addresses", RelationAddress, "All addresses of the relation.").WithItems(
                null,
                PoBox(150, "2990 AD", "Barendrecht", "t.a.v. crediteurenadministratie")),
            Obj("PostalAddress", RelationAddress, "Computed by OpenTMS: the first address whose type is the postal address.")
                .From(o => FirstAddress(o, "IsPostalAddress")),
            Obj("VisitingAddress", RelationAddress, "Computed by OpenTMS: the first address whose type is the visiting address.")
                .From(o => FirstAddress(o, "IsVisitingAddress")));

        // Relation personas: one key set for all, so a persona replaces another completely.

        private static readonly SampleSet Me = Company(
            "2f7c1d9e-8a4b-4c3d-9e5f-0a1b2c3d4e01", "Voorbeeld Transport B.V.", "VOORBEELDTRANSPORT",
            "voorbeeldtransport.nl", "+31 10 123 4567", "24123456", "NL801234567B01", "NL20INGB0001234567", "ING Bank",
            "INGBNL2A", "VTR", "D10001",
            Street("Waalhaven Zuidzijde", 45, "B", "3089 JH", "Rotterdam", "Kantoor en loods", 51.8890, 4.4310),
            PoBox(5050, "3008 AB", "Rotterdam", "t.a.v. administratie"));

        private static readonly SampleSet Customer = Company(
            "6d2b7e1a-3c4f-4a8e-9b71-2f5e8c0d4a11", "Klant Foods B.V.", "KLANTFOODS", "klantfoods.nl", "+31 180 612 345",
            "24412345", "NL812345678B01", "NL91ABNA0417164300", "ABN AMRO", "ABNANL2A", "KLF", "D10045",
            Street("Dierensteinweg", 30, "A", "2991 XJ", "Barendrecht", "Unit 3", 51.8576, 4.5328),
            PoBox(150, "2990 AD", "Barendrecht", "t.a.v. crediteurenadministratie"));

        private static readonly SampleSet LoadingLocation = Company(
            "91c3e5a7-0b2d-4f48-a6e1-7d9c3b5f2e10", "DC Venlo Klant Foods", "DCVENLO", "klantfoods.nl", "+31 77 323 4500",
            "24412345", "NL812345678B01", "NL91ABNA0417164300", "ABN AMRO", "ABNANL2A", "KLV", "D10046",
            Street("Celsiusweg", 12, "", "5928 PR", "Venlo", "Dock 14-22", 51.3920, 6.1540),
            PoBox(3010, "5902 RA", "Venlo", "t.a.v. expeditie"));

        private static readonly SampleSet UnloadingLocation = Company(
            "a8d4f6b2-1c3e-4a59-b7f2-8e0d4c6a3f21", "Supermarkt Centrum Utrecht", "SUPERMARKTUTRECHT",
            "supermarktutrecht.nl", "+31 30 231 9900", "30123456", "NL823456789B01", "NL02RABO0123456789", "Rabobank",
            "RABONL2U", "SCU", "D10112",
            Street("Vredenburg", 40, "2", "3511 BD", "Utrecht", "Laad- en losdock achterzijde", 52.0927, 5.1145),
            PoBox(1100, "3500 BC", "Utrecht", "t.a.v. goederenontvangst"));

        private static readonly SampleSet Carrier = Company(
            "5e9a7c3b-2d1f-4e60-8b94-1f3e5a7c9d32", "Charter Transport Venlo B.V.", "CHARTERVENLO",
            "chartertransportvenlo.nl", "+31 77 351 2200", "12054321", "NL834567890B01", "NL55RABO0300400500", "Rabobank",
            "RABONL2U", "CTV", "C20017",
            Street("Edisonstraat", 8, "", "5928 PG", "Venlo", "Poort 2", 51.3895, 6.1602),
            PoBox(4040, "5902 EA", "Venlo", "t.a.v. planning"));

        private static readonly SampleSet DriverJan = Company(
            "0c6e8a1f-3b5d-4c72-9a8e-2b4d6f8a0c43", "Jan de Vries", "DEVRIESJ", "chartertransportvenlo.nl",
            "+31 6 34567890", "", "", "NL69INGB0123456789", "ING Bank", "INGBNL2A", "", "",
            Street("Dorpsstraat", 12, "2", "5961 AB", "Horst", "Bovenwoning", 51.4542, 6.0514),
            PoBox(12, "5960 AA", "Horst", ""));

        private static readonly SampleSet DriverPieter = Company(
            "1d7f9b2a-4c6e-4d83-8b9f-3c5e7a9b1d54", "Pieter Jansen", "JANSENP", "chartertransportvenlo.nl",
            "+31 6 45678901", "", "", "NL44ABNA0987654321", "ABN AMRO", "ABNANL2A", "", "",
            Street("Kerkstraat", 7, "", "5911 HH", "Venlo", "", 51.3704, 6.1724),
            PoBox(77, "5900 AB", "Venlo", ""));

        // ------------------------------------------------------------------ small master data

        private static readonly CatalogueFragment ContactPerson = F("ContactPerson",
            Str("FirstName", "First name.", "Sanne"),
            Str("MiddleName", "Middle name (tussenvoegsel).", "van"),
            Str("LastName", "Last name.", "Dijk"),
            Str("JobTitle", "Job title.", "Logistiek planner"),
            Str("Department", "Department.", "Planning"),
            Str("EMailAddress", "E-mail address.", "s.vandijk@klantfoods.nl"),
            Str("TelephoneNumber", "Telephone number.", "+31 180 612 350"),
            Str("MobileNumber", "Mobile number.", "+31 6 23456789"));

        private static readonly CatalogueFragment UserAccount = F("UserAccount",
            Str("UserName", "User name.", "m.jansen"),
            Str("Name", "First name.", "Marieke"),
            Str("Surname", "Surname.", "Jansen"),
            Str("EmailAddress", "E-mail address.", "m.jansen@voorbeeldtransport.nl"));

        private static readonly SampleSet SecondUser = new()
        {
            ["UserName"] = "r.bakker", ["Name"] = "Rob", ["Surname"] = "Bakker", ["EmailAddress"] = "r.bakker@voorbeeldtransport.nl"
        };

        private static readonly CatalogueFragment Currency = F("Currency",
            Str("Name", "Currency name.", "Euro"),
            Str("Iso4217_3", "ISO 4217 code.", "EUR"),
            Str("Sign", "Currency sign (used as prefix of the CurrencyFormatted* fields).", "€"));

        private static readonly CatalogueFragment Vat = F("Vat",
            Str("Name", "VAT rate name.", "Hoog"),
            Str("Description", "VAT rate description.", "BTW hoog tarief"),
            Dec("Percentage", "VAT percentage (21 = 21%).", 21.0));

        private static readonly CatalogueFragment PriceList = F("PriceList",
            Str("Name", "Price list name.", "Tarieven 2026"));

        private static readonly CatalogueFragment CustomProperty = F("CustomProperty",
            Str("CustomPropertyDisplayName", "Display name the tenant gave the custom field.", "Klantreferentie"),
            Str("CustomPropertyValue", "Value of the custom field.", "KR-55120"));

        private static readonly CatalogueFragment OrderCustomProperty = F("OrderCustomProperty",
            Str("CustomPropertyDisplayName", "Display name the tenant gave the order custom field.", "Klantreferentie"),
            Values("CustomPropertyValues", ReportDataFieldType.String,
                "The non-empty values of this field on all orders in the transport file.", "KR-55120", "KR-55187"));

        private static readonly CatalogueFragment TransportUnit = F("TransportUnit",
            Str("Name", "Transport unit name.", "EUR"),
            Str("SutcStandardCode", "SUTC standard code.", "EUR"),
            Str("Description", "Description.", "Europallet 120x80"),
            Dec("ConversionFactorForEpq", "Conversion factor to euro pallet places.", 1.0),
            Dec("ConversionFactorForLoadingMeters", "Conversion factor to loading meters.", 0.4),
            Span("TransactionTime", "Handling time per unit.", "00:02:00"),
            Bool("RegisterTransactions", "Packaging balance transactions are registered for this unit.", true));

        private static readonly SampleSet RollContainer = new()
        {
            ["Name"] = "RC", ["SutcStandardCode"] = "RC", ["Description"] = "Rolcontainer", ["ConversionFactorForEpq"] = 0.5,
            ["ConversionFactorForLoadingMeters"] = 0.2, ["TransactionTime"] = "00:01:00", ["RegisterTransactions"] = true
        };

        private static readonly CatalogueFragment Product = F("Product",
            Str("Name", "Product name.", "ZUIVEL"),
            Str("Description", "Product description.", "Zuivelproducten"),
            Span("TransactionTime", "Handling time.", "00:05:00"));

        private static readonly SampleSet Vegetables = new()
        {
            ["Name"] = "AGF", ["Description"] = "Aardappelen, groente en fruit", ["TransactionTime"] = "00:04:00"
        };

        private static readonly CatalogueFragment ProductRelationCode = F("ProductRelationCode",
            Str("Name", "The customer's product code.", "KF-ZUIVEL"),
            Str("Description", "Description.", "Zuivel Klant Foods"),
            Obj("Relation", Relation, "The customer the code belongs to."),
            Obj("TransportUnit", TransportUnit, "Default transport unit of the code."),
            Obj("Product", Product, "The product the code stands for."));

        private static readonly CatalogueFragment TemperatureRange = F("TemperatureRange",
            Str("Name", "Temperature range name.", "Gekoeld 2-7 °C"),
            Dec("MinTemperature", "Minimum temperature (°C).", 2.0),
            Dec("MaxTemperature", "Maximum temperature (°C).", 7.0));

        private static readonly CatalogueFragment VehicleType = F("VehicleType",
            Id("Id", "Id of the vehicle type."),
            Str("Name", "Vehicle type name.", "Koeloplegger"),
            Str("Description", "Description.", "Trekker met koeloplegger"),
            Bool("IsDefault", "The tenant's default vehicle type.", true),
            Dec("MaxEpq", "Maximum euro pallet places.", 33.0),
            Dec("MaxLoadingMeters", "Maximum loading meters.", 13.6),
            En("DefaultFuelType", "Default fuel type, integer (OpenTMS FuelType: 0 Unknown, 1 Other, 2 Battery, 3 Biodiesel, "
                + "4 Diesel, 5 DieselBatteryHybrid, 6 Ethanol, 7 Hydrogen, 8 LiquidGas, 9 Lpg, 10 Methane, 11 Petrol, "
                + "12 PetrolBatteryHybrid, 13 NotApplicable).", 4),
            Dec("DefaultLength", "Default length (m).", 16.5),
            Dec("DefaultHeight", "Default height (m).", 4.0),
            Dec("DefaultWidth", "Default width (m).", 2.6),
            Dec("DefaultGrossVehicleWeight", "Default gross vehicle weight (kg).", 50000.0),
            Dec("SpeedCorrectionFactor", "Speed correction factor for route calculation.", 0.95));

        private static readonly CatalogueFragment TimeSlot = F("TimeSlot",
            Id("Id", "Id of the time slot.", "e2a4c6f8-0b1d-4e3f-a5b7-c9d1e3f5a7b9"),
            Str("Name", "Time slot name.", "Ochtend"),
            Str("Description", "Description.", "06:00 - 10:00"),
            Span("From", "Start of the slot.", "06:00:00"),
            Span("Till", "End of the slot.", "10:00:00"));

        private static readonly SampleSet Afternoon = new()
        {
            ["Id"] = "f3b5d7a9-1c2e-4f40-b6c8-d0e2f4a6b8c0", ["Name"] = "Middag", ["Description"] = "12:00 - 16:00",
            ["From"] = "12:00:00", ["Till"] = "16:00:00"
        };

        private static readonly CatalogueFragment WaypointType = F("WaypointType",
            Id("Id", "Id of the waypoint type.", "b1d3f5a7-9c0e-4b2d-8f4a-6c8e0a2c4e61"),
            Str("Name", "Waypoint type name.", "Laden"),
            Str("Description", "Description.", "Laadadres"),
            Str("SutcStandardCode", "SUTC standard code.", "PICKUP"));

        private static readonly SampleSet Unloading = new()
        {
            ["Id"] = "c2e4a6b8-0d1f-4c3e-9a5b-7d9f1b3d5f72", ["Name"] = "Lossen", ["Description"] = "Losadres",
            ["SutcStandardCode"] = "DELIVERY"
        };

        // ------------------------------------------------------------------ Vehicle

        private static readonly CatalogueFragment Vehicle = F("Vehicle",
            Id("Id", "Id of the vehicle."),
            Str("Name", "Vehicle name.", "Truck 12"),
            Str("LicencePlate", "Licence plate.", "12-BXL-3"),
            Str("Description", "Description.", "DAF XF 480 FT"),
            Obj("TransportCompany", Relation, "Owner of the vehicle.", Carrier),
            Obj("VehicleType", VehicleType, "Vehicle type."),
            List("DefaultDrivers", Relation, "Default drivers of the vehicle (relations).").WithItems(DriverJan, DriverPieter),
            Str("Reference", "Reference.", "V12"),
            Str("FuelType", "Fuel type as localized text (OpenTMS converted the enum to the translated name).", "Diesel"),
            Dec("Length", "Length (m).", 6.2),
            Dec("Height", "Height (m).", 3.95),
            Dec("Width", "Width (m).", 2.55),
            Dec("GrossVehicleWeight", "Gross vehicle weight (kg).", 20500.0),
            Bool("IsPrimaryVehicle", "The vehicle can be the primary vehicle (a truck or tractor).", true),
            Unexpanded("DefaultSecondaryVehicle", "Default secondary vehicle: always null in OpenTMS (never filled); "
                + "a Vehicle in shape, catalogued without children (recursion stop)."),
            Unexpanded("DefaultThirdVehicle", "Default third vehicle: always null in OpenTMS (never filled); "
                + "a Vehicle in shape, catalogued without children (recursion stop)."),
            Dec("MaxEpq", "Maximum euro pallet places.", 33.0),
            Dec("MaxLoadingMeters", "Maximum loading meters.", 13.6),
            Dec("SpeedCorrectionFactor", "Speed correction factor.", 1.0),
            Str("CostCenter", "Cost center.", "KP-210"));

        private static readonly SampleSet Trailer = new()
        {
            ["Name"] = "Oplegger 31", ["LicencePlate"] = "OB-45-KL", ["Description"] = "Schmitz koeloplegger",
            ["Reference"] = "O31", ["Length"] = 13.6, ["Height"] = 4.0, ["Width"] = 2.6, ["GrossVehicleWeight"] = 39000.0,
            ["IsPrimaryVehicle"] = false, ["CostCenter"] = "KP-220"
        };

        private static readonly SampleSet SecondTruck = new()
        {
            ["Name"] = "Truck 15", ["LicencePlate"] = "57-BKR-9", ["Description"] = "Volvo FH 460", ["Reference"] = "V15",
            ["CostCenter"] = "KP-211"
        };

        private static readonly SampleSet SecondTrailer = new()
        {
            ["Name"] = "Aanhanger 7", ["LicencePlate"] = "WN-71-PL", ["Description"] = "Middenas aanhanger",
            ["Reference"] = "A07", ["Length"] = 8.2, ["Height"] = 3.9, ["Width"] = 2.55, ["GrossVehicleWeight"] = 18000.0,
            ["IsPrimaryVehicle"] = false, ["MaxEpq"] = 15.0, ["MaxLoadingMeters"] = 7.5, ["CostCenter"] = "KP-220"
        };

        // ------------------------------------------------------------------ freight

        private static readonly CatalogueFragment Waypoint = F("TransportOrderFreightWaypoint",
            Int("OrderNumber", "Sequence of the waypoint within the freight item (1 = origin).", 1),
            Span("TransactionTime", "Planned handling time at the waypoint.", "00:20:00"),
            Day("Date", "Date of the waypoint.", "2026-10-20"),
            Obj("WaypointType", WaypointType, "Waypoint type (loading, unloading, ...)."),
            Obj("Relation", Relation, "The address relation of the waypoint.", LoadingLocation),
            Obj("TimeSlot", TimeSlot, "Time slot at the waypoint."),
            Obj("ContactPerson", ContactPerson, "Contact person at the waypoint."),
            Str("Reference", "Reference at the waypoint (e.g. loading number).", "LAAD-4711"),
            Str("Remarks", "Remarks at the waypoint.", "Melden bij portier, dock 14"));

        private static SampleSet LoadAt(string date, string reference) => new()
        {
            ["OrderNumber"] = 1, ["Date"] = date, ["Reference"] = reference
        };

        private static SampleSet UnloadAt(string date, string reference) => new()
        {
            ["OrderNumber"] = 2, ["TransactionTime"] = "00:15:00", ["Date"] = date, ["WaypointType"] = Unloading,
            ["Relation"] = UnloadingLocation, ["TimeSlot"] = Afternoon, ["Reference"] = reference,
            ["Remarks"] = "Lossen via achteringang"
        };

        private const string OrderCustomPropertyNote =
            "Order custom field {0} of the freight's order. Filled on the invoice and transport-file paths; OpenTMS left it "
            + "null in TransportOrder.FreightItems and in FreightItemsByLeg Goods.";

        private static readonly CatalogueMember[] FreightGoodMembers =
        [
            Id("Id", "Id of the freight item.", "a1f0c2d3-4e5b-4c6d-8e7f-9a0b1c2d3e41"),
            Int("OrderNumber", "1-based sequence number of the freight item on its document (order, invoice line, leg). "
                + "OpenTMS always sent 1 in TransportOrder.FreightItems (bug); the sample numbers 1, 2.", 1),
            Obj("TransportUnit", TransportUnit, "Transport unit of the freight."),
            Obj("ProductRelationCode", ProductRelationCode, "The customer's product code."),
            Obj("TemperatureRange", TemperatureRange, "Required temperature range."),
            Obj("Product", Product, "Product."),
            Obj("VehicleType", VehicleType, "Required vehicle type."),
            Dec("Amount", "Number of transport units.", 18.0),
            Dec("Positions", "Pallet positions.", 18.0),
            Dec("Colli", "Number of colli.", 540.0),
            Dec("Volume", "Volume (m³).", 25.9),
            Dec("Weight", "Weight (kg).", 9450.0),
            Dec("Epq", "Euro pallet places.", 18.0),
            Dec("LoadingMeters", "Loading meters.", 7.2),
            Dec("LoadFactor", "Load factor (0-1).", 0.53),
            Span("Duration", "Driving duration.", "02:10:00"),
            Dec("Distance", "Distance (km).", 168.4),
            Dec("Length", "Length of one unit (m).", 1.2),
            Dec("Width", "Width of one unit (m).", 0.8),
            Dec("Height", "Height of one unit (m).", 1.5),
            Str("InvoiceReference", "Reference printed on the invoice.", "PO-778812-1"),
            Str("BarCode", "Barcode (SSCC/EAN).", "387123450000000017"),
            Str("ContainerNumber", "Container number.", "MSKU1234565"),
            Str("SizeOrIso", "Container size or ISO type.", "45R1"),
            Str("Pincode", "Container pin code.", "4711"),
            Str("ShippingCompany", "Shipping company.", "Maersk"),
            Str("Remarks", "Remarks.", "Niet stapelen"),
            Str("InstructionsForwarder", "Instructions for the forwarder.", "Temperatuurlogger meesturen"),
            .. CustomProperties("CustomProperty", "Custom field {0} of the freight item: display name and value.", false),
            .. CustomProperties("OrderCustomProperty", OrderCustomPropertyNote, false),
            Dec("FreightPrice", "Sales price of the freight item, rounded to 2 decimals.", 412.5),
            Str("TransportOrderNumber", "Order number of the freight's transport order.", OrderNumber),
            Str("TransportOrderReference", "Customer reference of the freight's transport order.", OrderReference),
            Obj("Customer", Relation, "Customer of the freight's transport order.")
        ];

        private static readonly CatalogueFragment FreightGood = F("TransportOrderFreightGood", FreightGoodMembers);

        private static readonly CatalogueFragment Freight = F("TransportOrderFreight",
            FreightGoodMembers,
            List("Waypoints", Waypoint, "Waypoints of the freight item, ordered by OrderNumber.")
                .WithItems(LoadAt("2026-10-20", "LAAD-4711"), UnloadAt("2026-10-20", "LOS-0815")),
            Obj("OriginWaypoint", Waypoint, "Computed by OpenTMS: the waypoint with the lowest OrderNumber.")
                .From(o => EdgeWaypoint(o, first: true)),
            Obj("DestinationWaypoint", Waypoint, "Computed by OpenTMS: the waypoint with the highest OrderNumber.")
                .From(o => EdgeWaypoint(o, first: false)));

        private static readonly SampleSet SecondFreight = new()
        {
            ["Id"] = "b2a1d3e4-5f6c-4d7e-9f80-a1b2c3d4e552", ["Product"] = Vegetables,
            ["TemperatureRange"] = new SampleSet { ["Name"] = "Gekoeld 4-10 °C", ["MinTemperature"] = 4.0, ["MaxTemperature"] = 10.0 },
            ["ProductRelationCode"] = new SampleSet
            {
                ["Name"] = "KF-AGF", ["Description"] = "AGF Klant Foods", ["Product"] = Vegetables
            },
            ["Amount"] = 8.0, ["Positions"] = 8.0, ["Colli"] = 320.0, ["Volume"] = 11.5, ["Weight"] = 3600.0, ["Epq"] = 8.0,
            ["LoadingMeters"] = 3.2, ["LoadFactor"] = 0.24, ["Height"] = 1.8, ["InvoiceReference"] = "PO-778812-2",
            ["BarCode"] = "387123450000000024", ["ContainerNumber"] = "MSKU7654321", ["Pincode"] = "4712",
            ["Remarks"] = "Kantelgevoelig", ["FreightPrice"] = 287.5
        };

        private static SampleSet FreightItem(SampleSet? persona, int orderNumber, string date, string loadReference) =>
            SampleSet.Merge(persona, new SampleSet
            {
                ["OrderNumber"] = orderNumber,
                ["Waypoints"] = new[] { LoadAt(date, loadReference), UnloadAt(date, "LOS-0815") }
            })!;

        private static readonly CatalogueFragment FreightGroup = F("TransportOrderFreightGroup",
            List("Waypoints", Waypoint, "The waypoints of the group's first freight item."),
            List("Goods", FreightGood, "The freight items of the group (without waypoints)."),
            Obj("OriginWaypoint", Waypoint, "Computed by OpenTMS: the group waypoint with the lowest OrderNumber."),
            Obj("DestinationWaypoint", Waypoint, "Computed by OpenTMS: the group waypoint with the highest OrderNumber."));

        // ------------------------------------------------------------------ TransportOrder

        private static readonly CatalogueFragment AdditionalService = F("AdditionalService",
            Id("Id", "Id of the additional service line."),
            Str("LedgerAccount", "General ledger account.", "8020"),
            Str("CostCenter", "Cost center.", "KP-100"),
            Str("Description", "Description.", "Wachttijd (uur)"),
            Dec("Amount", "Quantity.", 1.5),
            Dec("UnitPrice", "Unit price.", 45.0),
            Dec("Price", "Line price.", 67.5));

        private static readonly CatalogueFragment DiscountAndSurcharge = F("DiscountAndSurcharge",
            Str("Name", "Name.", "Dieseltoeslag"),
            Str("Description", "Description.", "Dieseltoeslag oktober 2026"),
            Dec("Value", "Percentage or amount (negative for a discount).", 8.5),
            Bool("ValueIsPercentage", "Value is a percentage of the price.", true),
            Bool("ValueIsPricePerKilometer", "Value is a price per kilometer (billed as distance freight price). "
                + "Always false on the invoice path (OpenTMS did not copy it there).", false),
            Bool("ApplyAfterOtherDiscountAndSurcharges", "Applied to the price after the other discounts and surcharges.", false),
            Str("GeneralLedgerAccount", "General ledger account.", "8030"));

        private static readonly SampleSet LoyaltyDiscount = new()
        {
            ["Name"] = "Korting vaste klant", ["Description"] = "Jaarafspraak 2026", ["Value"] = -2.0,
            ["ValueIsPercentage"] = true, ["ValueIsPricePerKilometer"] = false, ["ApplyAfterOtherDiscountAndSurcharges"] = true,
            ["GeneralLedgerAccount"] = "8040"
        };

        private static readonly CatalogueFragment Revenue = F("TransportOrderRevenueDto",
            Str("Description", "Revenue line: \"Vast bedrag\", \"Vrachtprijs\", an additional service or a discount/surcharge "
                + "name (texts in the tenant language).", "Vrachtprijs"),
            Dec("Revenue", "Amount of the line.", 700.0));

        private static readonly CatalogueFragment TripOverview = F("TransportOrderTripOverviewDto",
            Str("FileNumber", "Number of a transport file (trip) carrying the order.", FileNumber),
            Moment("FileDate", "Date of the transport file (ISO date-time, no date-only converter in OpenTMS).", "2026-10-20T00:00:00"),
            Obj("TransportCompany", Relation, "Transport company of the trip.", Carrier),
            Obj("PrimaryVehicle", Vehicle, "Primary vehicle of the trip."));

        private static readonly CatalogueFragment TransportOrder = F("TransportOrder",
            Str("EntityNumber", "Computed by OpenTMS: the OrderNumber (used in file names).", null).From(o => Copy(o, "OrderNumber")),
            Obj("Relation", Relation, "The customer."),
            Obj("ContactPerson", ContactPerson, "Contact person at the customer."),
            Str("TransportOrderType", "Name of the order type.", "Distributie"),
            En("OrderState", "Order state, integer (OpenTMS TransportOrderState: -1 PendingAcceptance, 0 Open, 1 Canceled, "
                + "2 ReadyForInvoicing, 4 Invoiced, 8 Archived).", 0),
            Str("OrderNumber", "Order number.", OrderNumber),
            Day("OrderDate", "Order date.", "2026-10-16"),
            Str("OrderReference", "Customer reference.", OrderReference),
            Str("TemplateName", "Name of the order template the order was made from.", "Standaard gekoeld"),
            Obj("Currency", Currency, "Currency of the order."),
            Obj("Vat", Vat, "VAT rate of the order."),
            Int("PaymentTerm", "Payment term in days.", 30),
            Bool("SendFreightDocumentsAlongWithInvoice", "Freight documents go along with the invoice.", true),
            Str("InternalInvoicingRemarks", "Internal invoicing remarks.", "Factureren per week"),
            Str("InternalTransportOrderRemarks", "Internal order remarks.", "Klant belt bij vertraging"),
            ChargeBy("Amount", "amount", false),
            Bool("ChargeFixedPrice", "The order is charged a fixed price.", false),
            Dec("FixedPrice", "Fixed price (counted as revenue line \"Vast bedrag\" whenever it is not 0).", 0.0),
            ChargeBy("Positions", "pallet positions", false),
            ChargeBy("Colli", "colli", false),
            ChargeBy("Volume", "volume", false),
            ChargeBy("Weight", "weight", false),
            ChargeBy("Epq", "euro pallet places", true),
            ChargeBy("LoadingMeters", "loading meters", false),
            ChargeBy("LoadFactor", "load factor", false),
            ChargeBy("Distance", "distance", false),
            ChargeBy("Duration", "duration", false),
            ChargeBy("VehicleType", "vehicle type", false),
            ChargeBy("FreightItem", "freight item", false),
            CustomProperties("CustomProperty", "Custom field {0} of the order: display name and value.", false),
            List("AdditionalServices", AdditionalService, "Additional services on the order.").WithItems(
                null,
                new SampleSet { ["LedgerAccount"] = "8021", ["Description"] = "Laadklep", ["Amount"] = 1.0, ["UnitPrice"] = 25.0, ["Price"] = 25.0 }),
            List("DiscountAndSurcharges", DiscountAndSurcharge, "Discounts and surcharges on the order.").WithItems(null, LoyaltyDiscount),
            List("FreightItems", Freight, "Freight items, in creation order.").WithItems(
                FreightItem(null, 1, "2026-10-20", "LAAD-4711"),
                FreightItem(SecondFreight, 2, "2026-10-20", "LAAD-4711")),
            Dec("FreightPrice", "Computed: sum of the freight items' FreightPrice.", null).From(o => Money(Sum(o["FreightItems"], "FreightPrice"))),
            List("Revenue", Revenue, "Computed by OpenTMS: revenue lines (fixed price, freight price, additional services, "
                + "discounts and surcharges; percentage ones on the fixed or freight price, the \"apply after\" ones on the running "
                + "price; per-kilometer ones left out).").From(OrderRevenue),
            List("TripOverview", TripOverview, "The transport files (trips) carrying the order.").WithItems(
                null,
                new SampleSet { ["FileNumber"] = "R2026-0422", ["FileDate"] = "2026-10-21T00:00:00", ["PrimaryVehicle"] = SecondTruck }),
            Dec("TotalRevenue", "Computed: sum of the Revenue lines.", null).From(o => Money(Sum(o["Revenue"], "Revenue"))),
            List("FreightItemsByLeg", FreightGroup, "Computed by OpenTMS: the freight items grouped by origin and destination "
                + "relation and date; null when any freight item has other than 2 waypoints. The sample's two items share a "
                + "route, so it has one group with two Goods.").From(FreightGroups),
            Dec("Price", "Computed by OpenTMS: FixedPrice when ChargeFixedPrice, else FreightPrice.", null)
                .From(o => o["ChargeFixedPrice"]?.GetValue<bool>() == true ? Copy(o, "FixedPrice") : Copy(o, "FreightPrice")),
            AuditMembers("order", OrderId, "2026-10-16T09:12:33", "2026-10-19T15:40:02"));

        // ------------------------------------------------------------------ Invoice

        private static readonly CatalogueFragment InvoiceType = F("InvoiceType",
            Bool("IsDefault", "The tenant's default invoice type.", true),
            Str("Name", "Invoice type name.", "Verkoopfactuur"),
            Str("Description", "Description.", "Standaard verkoopfactuur"),
            Str("InvoiceNumberCreationOption", "Numbering option as the enum NAME (OpenTMS converted it to text): "
                + "FourDigitYearPrefixPlusIndexNumber, TwoDigitYearPrefixPlusIndexNumber, "
                + "FourDigitYearPrefixPlusMonthPrefixPlusIndexNumber, TwoDigitYearPrefixPlusMonthPrefixPlusIndexNumber, "
                + "IndexNumberOnly.", "FourDigitYearPrefixPlusIndexNumber"),
            Str("InvoiceNumberPrefix", "Invoice number prefix.", "F"),
            Str("InvoiceNumberFormat", "Format of the index number.", "00000"),
            Str("YearSuffix", "Separator after the year.", "-"),
            Str("MonthSuffix", "Separator after the month.", "-"));

        private static readonly CatalogueFragment TransportOrderAdditionalService = F("TransportOrderAdditionalService",
            Day("Date", "Date of the service.", "2026-10-20"),
            Str("Description", "Description.", "Wachttijd (uur)"),
            Dec("Amount", "Quantity.", 1.5),
            Dec("UnitPrice", "Unit price.", 45.0),
            Dec("Price", "Price.", 67.5));

        private static readonly CatalogueFragment InvoiceLine = F("InvoiceLine",
            Id("InvoiceId", "Id of the invoice.", InvoiceId),
            Int("OrderNo", "Line number.", 1),
            Id("TransportOrderId", "Id of the transport order the line comes from.", OrderId),
            Obj("TransportOrderFreight", Freight, "The invoiced freight item (freight lines); its OrderNumber is the line's "
                + "position. The sample fills it, AdditionalService and DiscountAndSurcharge on every line to show the shape.")
                .With(FreightItem(null, 1, "2026-10-20", "LAAD-4711")),
            Obj("AdditionalService", TransportOrderAdditionalService, "The invoiced additional service (service lines)."),
            Obj("DiscountAndSurcharge", DiscountAndSurcharge, "The invoiced discount or surcharge (discount/surcharge lines)."),
            Str("GeneralLedgerAccount", "General ledger account.", "8000"),
            Str("CostCenter", "Cost center.", "KP-100"),
            Str("Description", "Line description.", "Transport 2026-01234 Venlo - Utrecht"),
            FormattedAmounts("Amount", "Quantity", 1.0),
            FormattedAmounts("UnitPrice", "Unit price", 1250.0),
            FormattedAmounts("Price", "Line price, excluding VAT", 1250.0));

        private static readonly CatalogueFragment Invoice = F("Invoice",
            List("InvoiceLines", InvoiceLine, "Invoice lines, ordered by OrderNo.").WithItems(
                null,
                new SampleSet
                {
                    ["OrderNo"] = 2,
                    ["TransportOrderFreight"] = SampleSet.Merge(
                        FreightItem(SecondFreight, 2, "2026-10-13", "LAAD-4688"),
                        new SampleSet { ["TransportOrderNumber"] = "2026-01198", ["TransportOrderReference"] = "PO-778790" }),
                    ["AdditionalService"] = new SampleSet
                    {
                        ["Date"] = "2026-10-13", ["Description"] = "Wachttijd (uur)", ["Amount"] = 2.0, ["UnitPrice"] = 37.5, ["Price"] = 75.0
                    },
                    ["DiscountAndSurcharge"] = LoyaltyDiscount,
                    ["GeneralLedgerAccount"] = "8010",
                    ["Description"] = "Wachttijd 2026-01198 Venlo",
                    ["Amount"] = 2.0, ["UnitPrice"] = 37.5, ["Price"] = 75.0
                }),
            En("Flags", "Colour flags, integer bit mask (1 Red, 2 Green, 4 Blue, 8 Purple, 16 Orange, 32 Brown, 64 Grey, 128 Pink).", 0),
            Obj("InvoiceType", InvoiceType, "Invoice type."),
            Obj("Relation", Relation, "The invoiced customer."),
            Str("InvoiceTo", "Invoice addressee line.", "Klant Foods B.V. t.a.v. crediteurenadministratie"),
            Str("EMailAddress", "E-mail address the invoice is sent to.", "facturen@klantfoods.nl"),
            Str("EntityNumber", "Computed by OpenTMS: the InvoiceNumber (used in file names).", null).From(o => Copy(o, "InvoiceNumber")),
            Str("InvoiceNumber", "Invoice number.", "F2026-00815"),
            Day("InvoiceDate", "Invoice date.", "2026-10-21"),
            Str("FormattedInvoiceDate", "InvoiceDate as nl-NL short date (d-M-yyyy).", null).From(o => NlDate(Text(o["InvoiceDate"]))),
            Day("InvoiceDueDate", "Due date: InvoiceDate + PaymentTerm days.", null).From(DueDate),
            Str("FormattedInvoiceDueDate", "InvoiceDueDate as nl-NL short date (d-M-yyyy).", null).From(o => NlDate(Text(o["InvoiceDueDate"]))),
            Str("InvoiceReference", "Invoice reference.", "Week 42-43"),
            Moment("InvoiceSentAt", "When the invoice was sent.", "2026-10-21T10:15:00"),
            Moment("InvoiceExportedAt", "When the invoice was exported to accounting.", "2026-10-21T10:20:00"),
            Obj("Currency", Currency, "Currency."),
            Obj("Vat", Vat, "VAT rate."),
            Int("PaymentTerm", "Payment term in days.", 30),
            Id("ReportDefinitionId", "Id of the report definition the invoice prints with.", "d7f9b1c3-5e6a-4b8c-9d0e-1f2a3b4c5d66"),
            Str("Language", "Invoice language (culture name).", "nl"),
            Str("TermsAndConditions", "Terms and conditions text.",
                "Op al onze werkzaamheden zijn de Algemene Vervoerscondities 2002 (AVC) van toepassing."),
            Bool("SendFreightDocumentsAlongWithInvoice", "Freight documents go along with the invoice.", true),
            Str("InternalInvoicingRemarks", "Internal invoicing remarks.", "PO-nummer verplicht"),
            Dec("NetPrice", "Total INCLUDING VAT (TotalPrice + VatPrice, rounded to 2 decimals), despite the name: OpenTMS quirk "
                + "kept for template compatibility.", null).From(o => Money(InvoiceNet(o))),
            Str("FormattedNetPrice", "NetPrice formatted nl \"#,##0.00\".", null).From(o => Formatted(o, "NetPrice", false)),
            Str("CurrencyFormattedNetPrice", "NetPrice formatted with the currency sign, e.g. \"€ 1.603,25\".", null)
                .From(o => Formatted(o, "NetPrice", true)),
            Dec("TotalPrice", "Sum of the line prices, EXCLUDING VAT.", null).From(o => Money(InvoiceTotal(o))),
            Str("FormattedTotalPrice", "TotalPrice formatted nl \"#,##0.00\".", null).From(o => Formatted(o, "TotalPrice", false)),
            Str("CurrencyFormattedTotalPrice", "TotalPrice formatted with the currency sign.", null).From(o => Formatted(o, "TotalPrice", true)),
            Dec("VatPrice", "VAT amount: TotalPrice x Vat.Percentage / 100, rounded to 2 decimals.", null).From(o => Money(InvoiceVat(o))),
            Str("FormattedVatPrice", "VatPrice formatted nl \"#,##0.00\".", null).From(o => Formatted(o, "VatPrice", false)),
            Str("CurrencyFormattedVatPrice", "VatPrice formatted with the currency sign.", null).From(o => Formatted(o, "VatPrice", true)),
            Str("TransportOrderWeekNumbers", "Computed by OpenTMS: the distinct ISO week numbers of the lines' freight origin "
                + "dates, ascending, comma separated.", null).From(WeekNumbers),
            AuditMembers("invoice", InvoiceId, "2026-10-21T08:55:10", "2026-10-21T09:02:47"));

        // ------------------------------------------------------------------ TransportFile

        private static readonly CatalogueFragment Leg = F("TransportOrderFreightLeg",
            En("Flags", "Colour flags, integer bit mask (1 Red, 2 Green, 4 Blue, 8 Purple, 16 Orange, 32 Brown, 64 Grey, 128 Pink).", 0),
            Obj("TransportOrderFreight", Freight, "The freight item the leg moves; its OrderNumber is the leg's OrderNumber."),
            Int("OrderNumber", "1-based position of the leg in its list.", 1),
            Dec("Distance", "Distance (km).", 168.4),
            Span("Duration", "Driving duration.", "02:10:00"),
            Obj("OriginWaypointType", WaypointType, "Waypoint type at the origin."),
            Obj("OriginRelation", Relation, "Origin address relation.", LoadingLocation),
            Obj("OriginRelationContactPerson", ContactPerson, "Contact person at the origin."),
            Day("DepartureFromOrigin", "Departure date at the origin.", "2026-10-20"),
            Span("OriginTransactionTime", "Handling time at the origin.", "00:20:00"),
            Obj("OriginTimeSlot", TimeSlot, "Time slot at the origin."),
            Str("OriginReference", "Reference at the origin.", "LAAD-4711"),
            Str("OriginRemarks", "Remarks at the origin.", "Melden bij portier, dock 14"),
            Obj("DestinationWaypointType", WaypointType, "Waypoint type at the destination.", Unloading),
            Obj("DestinationRelation", Relation, "Destination address relation.", UnloadingLocation),
            Obj("DestinationRelationContactPerson", ContactPerson, "Contact person at the destination."),
            Day("ArrivalAtDestination", "Arrival date at the destination.", "2026-10-20"),
            Span("DestinationTransactionTime", "Handling time at the destination.", "00:15:00"),
            Obj("DestinationTimeSlot", TimeSlot, "Time slot at the destination.", Afternoon),
            Str("DestinationReference", "Reference at the destination.", "LOS-0815"),
            Str("DestinationRemarks", "Remarks at the destination.", "Lossen via achteringang"));

        private static readonly SampleSet FirstLeg = new()
        {
            ["TransportOrderFreight"] = FreightItem(null, 1, "2026-10-20", "LAAD-4711"),
            ["OrderNumber"] = 1
        };

        private static readonly SampleSet SecondLeg = new()
        {
            ["TransportOrderFreight"] = FreightItem(SecondFreight, 2, "2026-10-20", "LAAD-4712"),
            ["OrderNumber"] = 2, ["Distance"] = 168.4, ["OriginReference"] = "LAAD-4712", ["OriginTransactionTime"] = "00:10:00",
            ["DestinationRemarks"] = "AGF apart aanbieden"
        };

        private static readonly CatalogueFragment IncomingInvoiceType = F("IncomingInvoiceType",
            Bool("IsOutsourcingInvoiceType", "Invoices of this type are outsourcing (charter) invoices.", true),
            Str("Name", "Name.", "Charter"),
            Str("Description", "Description.", "Uitbesteed transport"));

        private static readonly CatalogueFragment IncomingInvoice = F("IncomingInvoice",
            Id("Id", "Id of the expected incoming invoice. OpenTMS sent the empty GUID on the transport-file path (never "
                + "filled there).", "e4c6a8f0-2b3d-4e5f-8a9b-0c1d2e3f4a75"),
            Obj("IncomingInvoiceType", IncomingInvoiceType, "Type of the incoming invoice."),
            Obj("Relation", Relation, "The supplier (carrier).", Carrier),
            Id("TransportFileId", "Id of the transport file it belongs to.", FileId),
            Day("ReferenceDate", "Reference date.", "2026-10-20"),
            Str("Reference", "Reference (discount/surcharge name for surcharge-based ones).", FileNumber),
            Dec("ExpectedPrice", "Expected price.", 640.0),
            Dec("ActualPrice", "Actual (invoiced) price.", 640.0),
            Dec("ExpectedDistance", "Expected distance (km).", 168.4),
            Dec("ActualDistance", "Actual distance (km).", 171.2),
            Dec("ExpectedAmount", "Expected quantity.", 1.0),
            Dec("ActualAmount", "Actual quantity.", 1.0),
            Str("PriceSpecification", "Price basis as localized text (e.g. \"Vast bedrag\", \"Prijs op basis van afstand\").", "Vast bedrag"),
            Moment("InvoiceDate", "Date of the supplier's invoice.", "2026-10-31T00:00:00"),
            Str("InvoiceNumber", "The supplier's invoice number.", "CTV-2026-1187"),
            Moment("InvoiceReceivedAt", "When the invoice was received.", "2026-11-02T08:14:00"),
            Moment("InvoiceApprovedAt", "When the invoice was approved.", "2026-11-03T10:30:00"),
            Str("Remarks", "Remarks.", "Conform afspraak"));

        private static readonly SampleSet WaitingTimeInvoice = new()
        {
            ["Id"] = "f5d7b9a1-3c4e-4f60-9b8c-1d2e3f4a5b86",
            ["IncomingInvoiceType"] = new SampleSet
            {
                ["IsOutsourcingInvoiceType"] = false, ["Name"] = "Wachttijd", ["Description"] = "Wachttijd charter"
            },
            ["Reference"] = "Wachttijd " + FileNumber, ["ExpectedPrice"] = 75.0, ["ActualPrice"] = 75.0,
            ["ExpectedDistance"] = 0.0, ["ActualDistance"] = 0.0, ["ExpectedAmount"] = 1.5, ["ActualAmount"] = 1.5,
            ["PriceSpecification"] = "Prijs op basis van aantallen", ["InvoiceNumber"] = "CTV-2026-1188", ["Remarks"] = "1,5 uur wachten in Venlo"
        };

        private static readonly CatalogueFragment Stop = F("TransportFileStop",
            Id("TransportFileId", "Id of the transport file.", FileId),
            Int("OrderNumber", "Position of the stop in the trip.", 1),
            Obj("WaypointType", WaypointType, "Waypoint type of the stop."),
            Obj("Relation", Relation, "Address relation of the stop.", LoadingLocation),
            Bool("IsOrigin", "Computed by OpenTMS: a leg starts at this stop (relation and waypoint type match its origin).", false)
                .From(o => JsonValue.Create(StopIsOrigin(o))),
            Str("DriverPortalUrl", "Driver portal link for this stop (token valid 14 days).",
                "https://voorbeeld.opentms.nl/driverportal/stop/eyJzdG9wIjoxfQ"),
            Moment("CalculatedTimeOfArrival", "Calculated arrival, in the stop's time zone.", "2026-10-20T07:45:00"),
            Dec("CalculatedCumulativeDistance", "Calculated cumulative distance (km).", 82.3),
            Moment("CalculatedTimeOfDeparture", "Calculated departure.", "2026-10-20T08:30:00"),
            Moment("EstimatedTimeOfArrival", "Estimated arrival (ETA).", "2026-10-20T07:50:00"),
            Moment("EstimatedTimeOfDeparture", "Estimated departure.", "2026-10-20T08:35:00"),
            Dec("EstimatedCumulativeDistance", "Estimated cumulative distance (km).", 82.3),
            Moment("ActualTimeOfArrival", "Actual arrival.", "2026-10-20T07:52:00"),
            Dec("ActualCumulativeDistance", "Actual cumulative distance (km).", 83.1),
            Moment("ActualTimeOfDeparture", "Actual departure.", "2026-10-20T08:41:00"),
            Str("EtaWindow", "Localized arrival text: the actual arrival time when known, else \"Tussen 07:30 en 08:00\" "
                + "(estimated or calculated arrival +/- the tenant's ETA margin).", "07:52"),
            List("Legs", Leg, "The legs handled at this stop (OrderNumber restarts at 1 per stop).").WithItems(FirstLeg, SecondLeg),
            List("Freight", Freight, "Computed by OpenTMS: the distinct freight items of the stop's legs.").From(StopFreight),
            Str("TransactionTime", "Computed by OpenTMS: the distinct handling times (hh:mm:ss) of the stop's legs, comma separated.", null)
                .From(o => StopJoin(o, "OriginTransactionTime", "DestinationTransactionTime", ", ")),
            Day("Date", "Computed by OpenTMS: earliest departure (origin stop) or arrival (destination stop) date of the stop's legs.", null)
                .From(StopDate),
            Str("TimeSlot", "Computed by OpenTMS: names of the stop's time slots, space separated.", null).From(StopTimeSlots),
            Str("Reference", "Computed by OpenTMS: the distinct references of the stop's legs, space separated.", null)
                .From(o => StopJoin(o, "OriginReference", "DestinationReference", " ")),
            Str("Remarks", "Computed by OpenTMS: the distinct remarks of the stop's legs, space separated.", null)
                .From(o => StopJoin(o, "OriginRemarks", "DestinationRemarks", " ")));

        private static readonly SampleSet UnloadingStop = new()
        {
            ["OrderNumber"] = 2, ["WaypointType"] = Unloading, ["Relation"] = UnloadingLocation,
            ["DriverPortalUrl"] = "https://voorbeeld.opentms.nl/driverportal/stop/eyJzdG9wIjoyfQ",
            ["CalculatedTimeOfArrival"] = "2026-10-20T10:30:00", ["CalculatedCumulativeDistance"] = 250.7,
            ["CalculatedTimeOfDeparture"] = "2026-10-20T11:15:00", ["EstimatedTimeOfArrival"] = "2026-10-20T10:40:00",
            ["EstimatedTimeOfDeparture"] = "2026-10-20T11:25:00", ["EstimatedCumulativeDistance"] = 250.7,
            ["ActualTimeOfArrival"] = "2026-10-20T10:41:00", ["ActualCumulativeDistance"] = 254.3,
            ["ActualTimeOfDeparture"] = "2026-10-20T11:20:00", ["EtaWindow"] = "10:41"
        };

        private static readonly CatalogueFragment ProductTotal = F("NameValuePair",
            Str("Name", "Product description and name (\"Description Name\", or Name alone).", "Zuivelproducten ZUIVEL"),
            Dec("Value", "Total Amount of the product's freight items.", 18.0));

        private static readonly CatalogueFragment Summary = F("TransportFileSummary",
            List("ProductTotals", ProductTotal, "Computed by OpenTMS: total Amount per product over the distinct freight of all stops."));

        private static readonly CatalogueFragment TransportFileType = F("TransportFileType",
            Bool("IsDefault", "The tenant's default transport file type.", true),
            Str("Name", "Name.", "Distributie"),
            Str("Description", "Description.", "Distributierit"),
            En("FileNumberCreationOption", "Numbering option, integer (0 FourDigitYearPrefixPlusIndexNumber, "
                + "1 TwoDigitYearPrefixPlusIndexNumber, 2 FourDigitYearPrefixPlusMonthPrefixPlusIndexNumber, "
                + "3 TwoDigitYearPrefixPlusMonthPrefixPlusIndexNumber, 4 IndexNumberOnly).", 0),
            Str("FileNumberPrefix", "File number prefix.", "R"),
            Str("FileNumberFormat", "Format of the index number.", "0000"),
            Str("YearSuffix", "Separator after the year.", "-"),
            Str("MonthSuffix", "Separator after the month.", "-"),
            Obj("DefaultTransportCompany", Relation, "Default transport company.", Me),
            Obj("DefaultDepartureRelation", Relation, "Default departure relation.", Me),
            Span("DefaultScheduledDepartureTime", "Default scheduled departure time.", "05:30:00"),
            Obj("DefaultArrivalRelation", Relation, "Default arrival relation.", Me),
            Bool("RecalculateOptimalRouteAutomaticly", "Route is re-optimized automatically (OpenTMS spelling).", true),
            En("DefaultRouteOptimizationMode", "Route optimization mode, integer (0 ShortestRouteToEmptyVehicle, "
                + "1 OptimizeRoundTrip, 2 SetScheduledTimeOfArrivalToLatestTimeslot).", 0),
            En("DefaultStopCreationMode", "Stop creation mode, integer (0 DistinctByLocationAndWaypoint, "
                + "1 DistinctByFreightItemWaypoint).", 0),
            Bool("KeepSynchronizedWithFms", "Kept in sync with the fleet management system.", false));

        private static readonly CatalogueFragment OrganizationUnit = F("OrganizationUnitDto",
            Int("Id", "Id of the organization unit (a number in OpenTMS).", 2),
            Int("ParentId", "Id of the parent unit.", 1),
            Str("Code", "Hierarchical code.", "00001.00002"),
            Str("DisplayName", "Display name.", "Vestiging Rotterdam"),
            Int("MemberCount", "Number of members.", 14),
            Moment("CreationTime", "When the unit was created.", "2024-03-04T10:00:00"),
            Int("CreatorUserId", "Id (number) of the creating user.", 1),
            Moment("LastModificationTime", "When the unit was last changed.", "2026-01-12T14:30:00"),
            Int("LastModifierUserId", "Id (number) of the last modifying user.", 1));

        private static readonly CatalogueFragment TransportFile = F("TransportFile",
            Str("EntityNumber", "Computed by OpenTMS: the FileNumber (used in file names).", null).From(o => Copy(o, "FileNumber")),
            List("Stops", Stop, "Stops of the trip, ordered by OrderNumber.").WithItems(null, UnloadingStop),
            List("Legs", Leg, "All legs of the trip.").WithItems(FirstLeg, SecondLeg),
            List("IncomingInvoices", IncomingInvoice, "Expected incoming (purchase) invoices of the trip.").WithItems(null, WaitingTimeInvoice),
            Obj("Summary", Summary, "Computed by OpenTMS: totals over the trip.").From(FileSummary),
            Dec("ExpectedOutsourcingInvoicePrice", "Computed by OpenTMS: sum of ExpectedPrice of the outsourcing-type incoming invoices.", null)
                .From(o => Money(Sum(OutsourcingInvoices(o), "ExpectedPrice"))),
            Str("ExpectedOutsourcingInvoicePriceSpecification", "Computed by OpenTMS: PriceSpecification of the first outsourcing-type incoming invoice.", null)
                .From(o => OutsourcingInvoices(o).FirstOrDefault()?["PriceSpecification"]?.DeepClone()),
            List("Drivers", Relation, "Drivers of the trip (relations).").WithItems(DriverJan, DriverPieter),
            Str("DriverNames", "Computed by OpenTMS: the distinct driver names, comma separated.", null).From(DriverNames),
            Dec("LoadCapacity", "Load capacity used, as calculated by OpenTMS.", 0.77),
            Span("EtaTimeMargin", "The tenant's ETA margin (used in Stops[].EtaWindow).", "00:15:00"),
            Str("FileNumber", "Transport file number.", FileNumber),
            En("Flags", "Colour flags, integer bit mask (1 Red, 2 Green, 4 Blue, 8 Purple, 16 Orange, 32 Brown, 64 Grey, 128 Pink).", 2),
            Bool("IsArchived", "The file is archived.", false),
            Bool("IsCanceled", "The file is canceled.", false),
            Obj("TransportFileType", TransportFileType, "Transport file type: OpenTMS never filled it (always null); shape listed for completeness."),
            Obj("OrganizationUnit", OrganizationUnit, "Organization unit: OpenTMS never filled it (always null); shape listed for completeness."),
            Day("FileDate", "File date (a yyyy-MM-dd string in OpenTMS).", "2026-10-20"),
            Str("FileName", "File name.", "Venlo - Utrecht koel"),
            Obj("DepartureRelation", Relation, "Departure relation (start of the trip).", Me),
            Moment("ScheduledTimeOfDeparture", "Scheduled departure.", "2026-10-20T05:30:00"),
            Str("FormattedScheduledTimeOfDeparture", "ScheduledTimeOfDeparture in the report language's general format (nl: d-M-yyyy HH:mm).", null)
                .From(o => NlDateTime(Text(o["ScheduledTimeOfDeparture"]))),
            Moment("ActualTimeOfDeparture", "Actual departure.", "2026-10-20T05:42:00"),
            Str("FormattedActualTimeOfDeparture", "ActualTimeOfDeparture in the report language's general format.", null)
                .From(o => NlDateTime(Text(o["ActualTimeOfDeparture"]))),
            Obj("ArrivalRelation", Relation, "Arrival relation (end of the trip).", Me),
            Moment("ScheduledTimeOfArrival", "Scheduled arrival.", "2026-10-20T15:00:00"),
            Str("FormattedScheduledTimeOfArrival", "ScheduledTimeOfArrival in the report language's general format.", null)
                .From(o => NlDateTime(Text(o["ScheduledTimeOfArrival"]))),
            Moment("ActualTimeOfArrival", "Actual arrival.", "2026-10-20T15:25:00"),
            Str("FormattedActualTimeOfArrival", "ActualTimeOfArrival in the report language's general format.", null)
                .From(o => NlDateTime(Text(o["ActualTimeOfArrival"]))),
            Dec("ScheduledDistance", "Scheduled distance (km).", 331.6),
            Dec("ActualDistance", "Actual distance (km).", 338.9),
            Obj("TransportCompany", Relation, "Transport company executing the trip.", Carrier),
            Obj("TransportCompanyContactPerson", ContactPerson, "Contact person at the transport company.",
                new SampleSet
                {
                    ["FirstName"] = "Kees", ["MiddleName"] = "", ["LastName"] = "Peeters", ["JobTitle"] = "Planner",
                    ["Department"] = "Planning", ["EMailAddress"] = "planning@chartertransportvenlo.nl",
                    ["TelephoneNumber"] = "+31 77 351 2201", ["MobileNumber"] = "+31 6 56789012"
                }),
            Obj("PrimaryVehicle", Vehicle, "Primary vehicle (truck or tractor)."),
            Obj("SecondaryVehicle", Vehicle, "Secondary vehicle (trailer).", Trailer),
            Obj("ThirdVehicle", Vehicle, "Third vehicle.", SecondTrailer),
            Str("Remarks", "Remarks.", "Koelketen bewaken, logger uitlezen"),
            Str("DriverInstructions", "Instructions for the driver.", "Temperatuur bij vertrek noteren op de vrachtbrief"),
            CustomProperties("CustomProperty", "Custom field {0} of the transport file: display name and value.", false),
            CustomProperties("OrderCustomProperty", "Order custom field {0}: display name and the values of all orders in the file.", true),
            Str("DriverPortalUrl", "Driver portal link for the trip (token valid 14 days).",
                "https://voorbeeld.opentms.nl/driverportal/file/eyJmaWxlIjoxfQ"),
            AuditMembers("transport file", FileId, "2026-10-17T13:20:00", "2026-10-20T15:31:12"));

        // ------------------------------------------------------------------ PurchaseOrder

        private static readonly CatalogueFragment PurchaseOrderLine = F("PurchaseOrderLine",
            Id("IncomingInvoiceId", "Id of the incoming invoice of the line.", null).From(o => o["IncomingInvoice"]?["Id"]?.DeepClone()),
            Obj("IncomingInvoice", IncomingInvoice, "The expected incoming invoice the line orders."),
            Int("OrderNo", "Line number.", 1),
            Id("TransportFileId", "Id of the transport file.", FileId),
            Str("TransportFileNumber", "Number of the transport file.", FileNumber),
            Str("Description", "Line description.", "Charter R2026-0421 Venlo - Utrecht"),
            Dec("Amount", "Quantity.", 1.0),
            Dec("UnitPrice", "Unit price.", 640.0),
            Dec("Price", "Line price; OpenTMS fell back to IncomingInvoice.ExpectedPrice when empty.", 640.0));

        private static readonly CatalogueFragment PurchaseOrder = F("PurchaseOrder",
            Str("EntityNumber", "Computed by OpenTMS: the OrderNumber (used in file names).", null).From(o => Copy(o, "OrderNumber")),
            Obj("Relation", Relation, "The supplier (carrier).", Carrier),
            Str("OrderNumber", "Purchase order number.", "IO2026-00117"),
            Moment("OrderDate", "Order date (ISO date-time: OpenTMS had no date-only converter here).", "2026-10-21T00:00:00"),
            Moment("SentAt", "When the purchase order was sent.", "2026-10-21T11:05:00"),
            Moment("SettledAt", "When the purchase order was settled.", "2026-11-04T09:30:00"),
            Obj("Currency", Currency, "Currency."),
            Str("Language", "Language (culture name).", "nl"),
            Obj("Vat", Vat, "VAT rate."),
            Int("PaymentTerm", "Payment term in days.", 30),
            Str("ExternalRemarks", "Remarks for the supplier.", "Graag ons inkoopordernummer op de factuur vermelden"),
            Dec("TotalPrice", "Computed by OpenTMS: sum of the line prices (ExpectedPrice of the incoming invoice when a line has "
                + "no price), excluding VAT.", null).From(PurchaseTotal),
            List("OrderLines", PurchaseOrderLine, "Order lines, ordered by OrderNo.").WithItems(
                null,
                new SampleSet
                {
                    ["IncomingInvoice"] = WaitingTimeInvoice, ["OrderNo"] = 2, ["Description"] = "Wachttijd R2026-0421 Venlo",
                    ["Amount"] = 1.5, ["UnitPrice"] = 50.0, ["Price"] = 75.0
                }),
            AuditMembers("purchase order", PurchaseOrderId, "2026-10-21T10:48:00", "2026-10-21T11:04:30"));

        // ------------------------------------------------------------------ TransportUnitBalance

        private static readonly CatalogueFragment Period = F("TransportUnitTransactionPeriod",
            En("Flags", "Colour flags, integer bit mask (1 Red, 2 Green, 4 Blue, 8 Purple, 16 Orange, 32 Brown, 64 Grey, 128 Pink).", 0),
            Str("Name", "Period name.", "Oktober 2026"),
            Str("Description", "Description.", "Emballageperiode oktober 2026"),
            Day("StartDate", "First day of the period.", "2026-10-01"),
            Day("EndDate", "Last day of the period; null while the period is open.", "2026-10-31"),
            Bool("IsClosed", "Computed by OpenTMS: EndDate is set.", false).From(o => JsonValue.Create(o["EndDate"] is not null)),
            Obj("Relation", Relation, "The relation of the period (" + PeriodRelationNote + ")."));

        private static readonly CatalogueFragment Transaction = F("TransportUnitTransaction",
            Day("TransactionDate", "Date of the transaction.", "2026-10-06"),
            Obj("SourceRelation", Relation, "Relation the units come from.", Me),
            Obj("SourceLocation", Relation, "Source location: OpenTMS filled it with the same relation as SourceRelation.", Me),
            Obj("DestinationRelation", Relation, "Relation the units go to."),
            Obj("DestinationLocation", Relation, "Destination location: OpenTMS filled it with the same relation as DestinationRelation."),
            Str("TransportFile", "Transport file number (text).", "R2026-0398"),
            Str("Receipt", "Receipt (bon) number.", "BON-5521"),
            Str("Reference", "Reference.", "Levering week 41"),
            Str("Remarks", "Remarks.", "Getekend door ontvanger"),
            Obj("TransportUnit", TransportUnit, "The transport unit (one transaction per unit: OpenTMS split multi-unit transactions)."),
            Dec("Amount", "Number of units moved.", 26.0));

        private static readonly SampleSet ReturnTransaction = new()
        {
            ["TransactionDate"] = "2026-10-14", ["SourceRelation"] = Customer, ["SourceLocation"] = Customer,
            ["DestinationRelation"] = Me, ["DestinationLocation"] = Me, ["TransportFile"] = "R2026-0412",
            ["Receipt"] = "BON-5560", ["Reference"] = "Retour week 42", ["Remarks"] = "Retour lege rolcontainers",
            ["TransportUnit"] = RollContainer, ["Amount"] = 6.0
        };

        private static readonly CatalogueFragment Balance = F("TransportUnitTransactionBalance",
            Obj("Relation", Relation, "Relation of the balance."),
            Obj("TransportUnit", TransportUnit, "Transport unit of the balance."),
            Dec("BeginBalance", "Balance at the start of the period.", 120.0),
            Dec("FinalBalance", "Balance at the end of the period; null while not calculated.", 94.0));

        private static readonly CatalogueFragment PeriodOverview = F("TransportUnitTransactionPeriodOverview",
            Obj("Period", Period, "The transport-unit (packaging) period."),
            List("Transactions", Transaction, "Transactions in the period, one per transport unit.").WithItems(null, ReturnTransaction),
            List("Balances", Balance, "Balances per relation and transport unit.").WithItems(
                null,
                new SampleSet { ["TransportUnit"] = RollContainer, ["BeginBalance"] = 40.0, ["FinalBalance"] = 46.0 }));

        private static readonly CatalogueFragment BalanceWrapper = F("GetSingleOutput`1",
            Obj("Entity", PeriodOverview, "The period overview. OpenTMS kept its GetSingleOutput wrapper, so templates read "
                + "Entity.Entity.…"));

        // ------------------------------------------------------------------ ConsignmentNote (design 56/58)

        /* The CMR payload Orders composes (OpenTms.Orders.ConsignmentNotes.ConsignmentNoteComposer): the boxes of
         * docs/input/transport-assessment/routing-co2-cmr.md §3.2 as fields. Parties are resolved per box source
         * (customer, origin/destination address, own company, carrier, shipper, trip departure/arrival, empty); the
         * goods rows carry every column a profile may show. Box numbers are in the descriptions. */

        private static readonly CatalogueFragment CmrAddress = F("CmrAddress",
            Str("Street", "Street.", "Celsiusweg"),
            Str("HouseNumber", "House number with its suffix.", "12"),
            Str("PostalCode", "Postal code.", "5928 PR"),
            Str("City", "City.", "Venlo"),
            Str("CountryCode", "ISO 3166-1 alpha-2 country code.", "NL"),
            Str("Line", "The address on one line: street and number, postal code and city, country.", "Celsiusweg 12, 5928 PR Venlo, NL"));

        private static readonly CatalogueFragment CmrParty = F("CmrParty",
            Id("RelationId", "Id of the Master Data relation; null for a party without relation (a carrier known by name only).", "91c3e5a7-0b2d-4f48-a6e1-7d9c3b5f2e10"),
            Str("Name", "Name of the party.", "DC Venlo Klant Foods"),
            Obj("Address", CmrAddress, "The address the box shows; null when the party has none."),
            Str("Source", "Which source filled the box (CmrPartySource name: Customer, OriginAddress, DestinationAddress, OwnCompany, "
                + "Carrier, Shipper, TripDeparture, TripArrival).", "OriginAddress"));

        private static readonly SampleSet CmrCustomer = new()
        {
            ["RelationId"] = "6d2b7e1a-3c4f-4a8e-9b71-2f5e8c0d4a11", ["Name"] = "Klant Foods B.V.", ["Source"] = "Customer",
            ["Address"] = new SampleSet { ["Street"] = "Dierensteinweg", ["HouseNumber"] = "30A", ["PostalCode"] = "2991 XJ", ["City"] = "Barendrecht", ["CountryCode"] = "NL", ["Line"] = "Dierensteinweg 30A, 2991 XJ Barendrecht, NL" }
        };

        private static readonly SampleSet CmrConsignee = new()
        {
            ["RelationId"] = "a8d4f6b2-1c3e-4a59-b7f2-8e0d4c6a3f21", ["Name"] = "Supermarkt Centrum Utrecht", ["Source"] = "DestinationAddress",
            ["Address"] = new SampleSet { ["Street"] = "Vredenburg", ["HouseNumber"] = "40", ["PostalCode"] = "3511 BD", ["City"] = "Utrecht", ["CountryCode"] = "NL", ["Line"] = "Vredenburg 40, 3511 BD Utrecht, NL" }
        };

        private static readonly SampleSet CmrCarrier = new()
        {
            ["RelationId"] = "5e9a7c3b-2d1f-4e60-8b94-1f3e5a7c9d32", ["Name"] = "Charter Transport Venlo B.V.", ["Source"] = "Carrier",
            ["Address"] = new SampleSet { ["Street"] = "Edisonstraat", ["HouseNumber"] = "8", ["PostalCode"] = "5928 PG", ["City"] = "Venlo", ["CountryCode"] = "NL", ["Line"] = "Edisonstraat 8, 5928 PG Venlo, NL" }
        };

        private static readonly CatalogueFragment CmrPlace = F("CmrPlace",
            Str("Name", "Name of the relation at the place.", "DC Venlo Klant Foods"),
            Str("City", "City.", "Venlo"),
            Str("CountryCode", "ISO 3166-1 alpha-2 country code.", "NL"),
            Str("Address", "The full address on one line.", "Celsiusweg 12, 5928 PR Venlo, NL"),
            Day("Date", "The date of the box (box 4: the first stop's date; box 21: the print date); null for box 3.", "2026-10-20"),
            Str("Source", "Which source filled the box (CmrPartySource name).", "OriginAddress"));

        private static readonly CatalogueFragment CmrSignature = F("CmrSignature",
            Str("Name", "Name of the signing party.", "Klant Foods B.V."),
            Str("Place", "Place of signing (the party's city).", "Barendrecht"),
            Day("Date", "Date of signing; null until the signed flow fills it.", null));

        private static readonly CatalogueFragment CmrGoodsRow = F("CmrGoodsRow",
            Id("OrderLineId", "Id of the order line the row prints.", "7a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d"),
            Int("Position", "Line position on the order.", 1),
            Str("ContainerNumber", "Container number.", "MSKU1234565"),
            Str("PinCode", "Container pin code.", "4711"),
            Str("SizeOrIso", "Container size or ISO code.", "45G1"),
            Dec("Quantity", "Quantity (OpenTMS Amount).", 26.0),
            Str("TransportUnit", "Transport unit name.", "Europallet"),
            Str("ProductCode", "The customer's product code.", "KF-ZUIVEL"),
            Str("Product", "Product name.", "Zuivelproducten"),
            Dec("Colli", "Colli.", 520.0),
            Dec("WeightKg", "Weight (kg).", 12480.0),
            Str("Dimensions", "Length × width × height (cm).", "120 × 80 × 180 cm"),
            Dec("VolumeM3", "Volume (m³).", 44.9),
            Str("Barcode", "Barcode of the line.", "8712345678906"),
            Dec("Positions", "Pallet positions.", 26.0),
            Str("Temperature", "Temperature range name.", "Gekoeld 2-7 °C"),
            Str("InvoiceReference", "Invoice reference of the line.", "INV-2026-0912"),
            Str("Description", "Description (the line's remarks).", "Gekoeld houden"));

        private static readonly SampleSet SecondGoodsRow = new()
        {
            ["OrderLineId"] = "8b2c3d4e-5f6a-4b7c-9d8e-0f1a2b3c4d5e", ["Position"] = 2, ["ContainerNumber"] = null, ["PinCode"] = null, ["SizeOrIso"] = null,
            ["Quantity"] = 4.0, ["TransportUnit"] = "Rolcontainer", ["ProductCode"] = "KF-AGF", ["Product"] = "Aardappelen, groente en fruit", ["Colli"] = 48.0,
            ["WeightKg"] = 960.0, ["Dimensions"] = null, ["VolumeM3"] = 3.2, ["Barcode"] = null, ["Positions"] = 2.0, ["Temperature"] = "Gekoeld 2-7 °C",
            ["InvoiceReference"] = null, ["Description"] = null
        };

        private static readonly CatalogueFragment CmrGoodsTotals = F("CmrGoodsTotals",
            Dec("Quantity", "Sum of the rows' quantities.", 30.0),
            Dec("Colli", "Sum of the rows' colli.", 568.0),
            Dec("WeightKg", "Sum of the rows' weights (kg).", 13440.0),
            Dec("VolumeM3", "Sum of the rows' volumes (m³).", 48.1),
            Dec("Positions", "Sum of the rows' pallet positions.", 28.0));

        private static readonly CatalogueFragment CmrTextItem = F("CmrTextItem",
            Str("Item", "The item the text came from (CmrInstructionItem or CmrRemarkItem name).", "ShipperInstructions"),
            Str("Text", "The text.", "Niet stapelen"));

        private static readonly CatalogueFragment CmrOrder = F("CmrOrder",
            Id("Id", "Id of the transport order.", OrderId),
            Str("Number", "Order number.", OrderNumber),
            Str("CustomerReference", "Customer reference.", OrderReference),
            Day("OrderDate", "Order date.", "2026-10-16"),
            Str("CustomerName", "Customer name.", "Klant Foods B.V."));

        private static readonly CatalogueFragment CmrTrip = F("CmrTrip",
            Id("Id", "Id of the transport trip.", FileId),
            Str("Number", "Trip number.", FileNumber),
            Day("TripDate", "Trip date.", "2026-10-20"),
            Str("CarrierName", "Carrier name.", "Charter Transport Venlo B.V."),
            Values("VehicleNames", ReportDataFieldType.String, "Names of the trip's vehicles.", "Truck 12", "Trailer 7"),
            Values("LicencePlates", ReportDataFieldType.String, "Licence plates of the trip's vehicles.", "12-BXL-3", "OT-45-PQ"),
            Values("DriverNames", ReportDataFieldType.String, "Names of the trip's drivers.", "Jan de Vries"));

        private static readonly CatalogueFragment ConsignmentNote = F("ConsignmentNote",
            Str("EntityNumber", "The consignment note number (used in file names).", "CMR-2026-000123"),
            Str("Number", "The consignment note number, drawn from Orders.ConsignmentNote on print.", "CMR-2026-000123"),
            Str("Barcode", "The barcode content (the number).", "CMR-2026-000123"),
            Int("Copies", "Copies to print (1–5).", 4),
            Bool("ShowBorders", "Print the box borders and static texts.", true),
            Bool("ShowQrCode", "Print the QR code of the barcode.", true),
            Bool("GroupByLeg", "Lines sharing the parties are grouped on one note.", false),
            Str("ProfileName", "Name of the profile used.", "Standaard"),
            Obj("Sender", CmrParty, "Box 1: sender.", CmrCustomer),
            Obj("Consignee", CmrParty, "Box 2: consignee.", CmrConsignee),
            Obj("DeliveryPlace", CmrPlace, "Box 3: place of delivery.", new SampleSet { ["Name"] = "Supermarkt Centrum Utrecht", ["City"] = "Utrecht", ["Address"] = "Vredenburg 40, 3511 BD Utrecht, NL", ["Date"] = null, ["Source"] = "DestinationAddress" }),
            Obj("TakingOverPlace", CmrPlace, "Box 4: place and date of taking over."),
            Str("AttachedDocuments", "Box 5: documents attached.", "Paklijst, CMR"),
            Values("GoodsColumns", ReportDataFieldType.String, "The goods columns the profile shows (CmrGoodsColumn names).", "Quantity", "TransportUnit", "Product", "WeightKg", "VolumeM3", "ColumnNames", "Totals"),
            Bool("ShowColumnNames", "Print the column headings of the goods table.", true),
            Bool("ShowTotals", "Print the totals row of the goods table.", true),
            List("Goods", CmrGoodsRow, "Boxes 6–12: one row per order line.").WithItems(null, SecondGoodsRow),
            Str("GoodsDescription", "The fixed descriptions chosen for the goods (box 6).", "Breekbaar"),
            Obj("Totals", CmrGoodsTotals, "The totals of the goods rows."),
            List("SenderInstructionItems", CmrTextItem, "Box 13 as items: what each text came from.").WithItems(null, new SampleSet { ["Item"] = "FixedDescriptions", ["Text"] = "Breekbaar" }),
            Str("SenderInstructions", "Box 13: sender's instructions, the items joined by newlines.", "Niet stapelen\nBreekbaar"),
            Str("PaymentInstruction", "Box 14: franking (PaymentInstruction name: Free, NotFree); null when not set.", "Free"),
            Obj("Carrier", CmrParty, "Box 16: carrier.", CmrCarrier),
            Obj("SuccessiveCarrier", CmrParty, "Box 17: successive carrier; null for the Empty source.", new SampleSet { ["RelationId"] = null, ["Name"] = "", ["Source"] = "Empty" }),
            List("CarrierRemarkItems", CmrTextItem, "Box 18 as items: what each text came from.").WithItems(
                new SampleSet { ["Item"] = "OrderNumber", ["Text"] = OrderNumber },
                new SampleSet { ["Item"] = "Vehicles", ["Text"] = "Truck 12 (12-BXL-3)\nTrailer 7 (OT-45-PQ)" }),
            Str("CarrierRemarks", "Box 18: carrier's remarks, the items joined by newlines.", OrderNumber + "\nTruck 12 (12-BXL-3)\nTrailer 7 (OT-45-PQ)"),
            Str("SpecialAgreements", "Box 19: special agreements.", "Levering voor 10:00"),
            Obj("EstablishedIn", CmrPlace, "Box 21: established in / on (the print date).", new SampleSet { ["Name"] = "Voorbeeld Transport B.V.", ["City"] = "Rotterdam", ["Address"] = "Waalhaven Zuidzijde 45B, 3089 JH Rotterdam, NL", ["Date"] = "2026-10-19", ["Source"] = "OwnCompany" }),
            Obj("SenderSignature", CmrSignature, "Box 22: signature of the sender."),
            Obj("CarrierSignature", CmrSignature, "Box 23: signature of the carrier.", new SampleSet { ["Name"] = "Charter Transport Venlo B.V.", ["Place"] = "Venlo" }),
            Obj("ConsigneeSignature", CmrSignature, "Box 24: signature of the consignee.", new SampleSet { ["Name"] = "Supermarkt Centrum Utrecht", ["Place"] = "Utrecht" }),
            Obj("Order", CmrOrder, "The order the note belongs to."),
            Obj("Trip", CmrTrip, "The trip the lines ride on; null when not planned yet."),
            Values("OrderLineIds", ReportDataFieldType.Guid, "Ids of the order lines on the note.", "7a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d", "8b2c3d4e-5f6a-4b7c-9d8e-0f1a2b3c4d5e"),
            Moment("PrintedAt", "When the note was printed.", "2026-10-19T15:40:02"),
            Str("PrintedBy", "User name of the printer.", "m.jansen"));

        // ------------------------------------------------------------------ envelope

        public static CatalogueFragment EnvelopeFor(ReportPurpose purpose, string entityType)
        {
            var (entity, description) = purpose switch
            {
                ReportPurpose.TransportOrderConfirmation => (TransportOrder, "The transport order."),
                ReportPurpose.TransportFile => (TransportFile, "The transport file (trip)."),
                ReportPurpose.Invoice => (Invoice, "The invoice."),
                ReportPurpose.PurchaseOrder => (PurchaseOrder, "The purchase order."),
                ReportPurpose.TransportUnitBalance => (BalanceWrapper, "Wrapper around the transport-unit period overview (Entity.Entity)."),
                ReportPurpose.ConsignmentNote => (ConsignmentNote, "The consignment note (CMR) of one order's lines (design 58)."),
                _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "No report data catalogue for this purpose.")
            };

            return F("RequestBodyDto",
                Str("CompanyLogo", "The tenant's company logo as a base64 data URL (data:image/png;base64,…), as OpenTMS stored it.", CompanyLogo),
                Obj("Me", Relation, "The tenant's own company (the relation marked as \"me\").", Me),
                Obj("Entity", entity, description),
                Str("EntityType", "OpenTMS payload class name of Entity.", entityType));
        }

        // ------------------------------------------------------------------ member and fragment helpers

        private static CatalogueFragment F(string name, params object[] parts)
        {
            var members = new List<CatalogueMember>();
            foreach (var part in parts)
            {
                switch (part)
                {
                    case CatalogueMember member: members.Add(member); break;
                    case IEnumerable<CatalogueMember> many: members.AddRange(many); break;
                    default: throw new InvalidOperationException($"Unexpected part {part.GetType().Name} in fragment {name}.");
                }
            }
            return new CatalogueFragment(name, members);
        }

        private static CatalogueMember Str(string name, string description, string? sample) =>
            new(name, ReportDataFieldType.String, description) { Sample = sample };

        private static CatalogueMember Int(string name, string description, long? sample) =>
            new(name, ReportDataFieldType.Integer, description) { Sample = sample };

        private static CatalogueMember Dec(string name, string description, double? sample) =>
            new(name, ReportDataFieldType.Decimal, description) { Sample = sample };

        private static CatalogueMember Bool(string name, string description, bool sample) =>
            new(name, ReportDataFieldType.Boolean, description) { Sample = sample };

        private static CatalogueMember Day(string name, string description, string? sample) =>
            new(name, ReportDataFieldType.Date, description) { Sample = sample };

        private static CatalogueMember Moment(string name, string description, string? sample) =>
            new(name, ReportDataFieldType.DateTime, description) { Sample = sample };

        private static CatalogueMember Span(string name, string description, string? sample) =>
            new(name, ReportDataFieldType.TimeSpan, description) { Sample = sample };

        /// <summary>A GUID member; without sample the builder derives a stable id from the sibling values.</summary>
        private static CatalogueMember Id(string name, string description, string? sample = null) =>
            new(name, ReportDataFieldType.Guid, description) { Sample = sample };

        private static CatalogueMember En(string name, string description, long sample) =>
            new(name, ReportDataFieldType.Enum, description) { Sample = sample };

        private static CatalogueMember Obj(string name, CatalogueFragment fragment, string description, SampleSet? overrides = null) =>
            new(name, ReportDataFieldType.Object, description) { Fragment = fragment, Overrides = overrides };

        /// <summary>An object member catalogued without children (recursion stop); its sample is null.</summary>
        private static CatalogueMember Unexpanded(string name, string description) =>
            new(name, ReportDataFieldType.Object, description);

        private static CatalogueMember List(string name, CatalogueFragment fragment, string description) =>
            new(name, ReportDataFieldType.Array, description) { Fragment = fragment };

        private static CatalogueMember Values(string name, ReportDataFieldType elementType, string description, params object?[] samples) =>
            new(name, ReportDataFieldType.Array, description) { ElementType = elementType, Elements = samples };

        private static IEnumerable<CatalogueMember> CustomProperties(string prefix, string descriptionFormat, bool orderValues) =>
            Enumerable.Range(0, 10).Select(i => Obj(
                prefix + i.ToString(CultureInfo.InvariantCulture),
                orderValues ? OrderCustomProperty : CustomProperty,
                string.Format(CultureInfo.InvariantCulture, descriptionFormat, i)
                    + " Null when the tenant has not defined the field.",
                orderValues
                    ? new SampleSet
                    {
                        ["CustomPropertyDisplayName"] = CustomPropertyNames[i],
                        ["CustomPropertyValues"] = new object?[] { CustomPropertyValues[i], SecondCustomPropertyValues[i] }
                    }
                    : new SampleSet
                    {
                        ["CustomPropertyDisplayName"] = CustomPropertyNames[i], ["CustomPropertyValue"] = CustomPropertyValues[i]
                    }));

        private static IEnumerable<CatalogueMember> ChargeBy(string basis, string text, bool charged) =>
        [
            Bool("ChargeBy" + basis, $"The order is charged by {text}.", charged),
            Obj("ChargeBy" + basis + "PriceList", PriceList, $"Price list used when charging by {text}.",
                new SampleSet { ["Name"] = "Tarief " + text + " 2026" })
        ];

        private static IEnumerable<CatalogueMember> FormattedAmounts(string name, string text, double sample) =>
        [
            Dec(name, text + ".", sample),
            Str("Formatted" + name, $"{name} formatted nl \"#,##0.00\" (e.g. \"1.250,00\").", null).From(o => Formatted(o, name, false)),
            Str("CurrencyFormatted" + name, $"{name} formatted with the currency sign (e.g. \"€ 1.250,00\").", null)
                .From(o => Formatted(o, name, true))
        ];

        private static IEnumerable<CatalogueMember> AuditMembers(string entity, string id, string created, string modified) =>
        [
            Id("Id", $"Id of the {entity}.", id),
            Moment("CreationTime", $"When the {entity} was created.", created),
            Obj("CreatorUser", UserAccount, $"User who created the {entity} (falls back to the user printing it)."),
            Moment("LastModificationTime", $"When the {entity} was last changed; null if never.", modified),
            Obj("LastModifierUser", UserAccount, $"User who last changed the {entity}; null if never.", SecondUser)
        ];

        private static SampleSet Company(
            string id, string name, string searchName, string domain, string phone, string chamberOfCommerceNo, string vatNo,
            string iban, string bankName, string bic, string carrierCode, string debtorNumber, SampleSet visiting, SampleSet postal) => new()
        {
            ["Id"] = id, ["Name"] = name, ["FullName"] = name, ["SearchName"] = searchName,
            ["ChamberOfCommerceNo"] = chamberOfCommerceNo, ["TelephoneNumber"] = phone, ["EMailAddress"] = "info@" + domain,
            ["Url"] = "https://www." + domain, ["VatNo"] = vatNo, ["Iban"] = iban, ["BankName"] = bankName, ["Bic"] = bic,
            ["EMailAddressForInvoices"] = "facturen@" + domain, ["CarrierCode"] = carrierCode, ["DebtorNumber"] = debtorNumber,
            ["InvoiceTo"] = name, ["Addresses"] = new[] { visiting, postal }
        };

        private static SampleSet Street(
            string street, int number, string suffix, string postalCode, string city, string line2, double latitude, double longitude) => new()
        {
            ["IsPostalAddress"] = false, ["IsVisitingAddress"] = true, ["PostalCode"] = postalCode, ["City"] = city,
            ["AddressLine1"] = street, ["AddressLine2"] = line2, ["Number"] = number, ["NumberSuffix"] = suffix,
            ["Longitude"] = longitude, ["Latitude"] = latitude
        };

        private static SampleSet PoBox(int number, string postalCode, string city, string line2) => new()
        {
            ["IsPostalAddress"] = true, ["IsVisitingAddress"] = false, ["PostalCode"] = postalCode, ["City"] = city,
            ["AddressLine1"] = "Postbus", ["AddressLine2"] = line2, ["Number"] = number, ["NumberSuffix"] = ""
        };

        // ------------------------------------------------------------------ computed values (OpenTMS getters)

        private static JsonNode? Copy(JsonObject o, string member) => o[member]?.DeepClone();

        private static string? Text(JsonNode? node) =>
            node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;

        private static double? Number(JsonNode? node) =>
            node is JsonValue value && value.GetValueKind() == JsonValueKind.Number
                ? double.Parse(value.ToJsonString(), CultureInfo.InvariantCulture)
                : null;

        private static bool Flag(JsonNode? node) => node is JsonValue value && value.GetValueKind() == JsonValueKind.True;

        private static IEnumerable<JsonObject> Items(JsonNode? node) =>
            node is JsonArray array ? array.OfType<JsonObject>() : [];

        private static double? Sum(JsonNode? array, string member) => Sum(Items(array), member);

        private static double? Sum(IEnumerable<JsonObject> items, string member) =>
            items.Select(item => Number(item[member])).Where(value => value.HasValue).Sum();

        private static JsonNode? Money(double? value) =>
            value.HasValue ? JsonValue.Create(Math.Round(value.Value, 2, MidpointRounding.AwayFromZero)) : null;

        private static string NlNumber(double value)
        {
            var invariant = value.ToString("#,##0.00", CultureInfo.InvariantCulture);
            return new string(invariant.Select(c => c switch { ',' => '.', '.' => ',', _ => c }).ToArray());
        }

        private static JsonNode? Formatted(JsonObject o, string member, bool withCurrency) =>
            Number(o[member]) is { } value ? JsonValue.Create((withCurrency ? "€ " : "") + NlNumber(value)) : null;

        private static JsonNode? NlDate(string? isoDate) =>
            isoDate is null ? null
                : JsonValue.Create(System.DateTime.ParseExact(isoDate, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                    .ToString("d-M-yyyy", CultureInfo.InvariantCulture));

        private static JsonNode? NlDateTime(string? isoDateTime) =>
            isoDateTime is null ? null
                : JsonValue.Create(System.DateTime.Parse(isoDateTime, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                    .ToString("d-M-yyyy HH:mm", CultureInfo.InvariantCulture));

        private static JsonNode? Kix(JsonObject o)
        {
            var postalCode = Text(o["PostalCode"]);
            if (string.IsNullOrWhiteSpace(postalCode)) return null;
            postalCode = new string(postalCode.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
            var iso = Text(o["Country"]?["Iso3166_2"]);
            if (o["Country"] is not null && !string.Equals(iso, "NL", StringComparison.OrdinalIgnoreCase))
            {
                return JsonValue.Create(iso + postalCode);
            }

            var kix = postalCode;
            if (Number(o["Number"]) is { } number)
            {
                kix += ((long)number).ToString(CultureInfo.InvariantCulture);
            }
            else if (Text(o["AddressLine1"]) is { Length: > 0 } line)
            {
                var digits = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Reverse().Select(Digits).FirstOrDefault(d => d.Length > 0);
                kix += digits;
            }

            if (Digits(Text(o["NumberSuffix"]) ?? "") is { Length: > 0 } suffix)
            {
                kix += "X" + suffix;
            }
            return JsonValue.Create(kix);
        }

        private static string Digits(string text) => new(text.Where(char.IsDigit).ToArray());

        private static JsonNode? FirstAddress(JsonObject relation, string flag) =>
            Items(relation["Addresses"]).FirstOrDefault(address => Flag(address[flag]))?.DeepClone();

        private static JsonNode? EdgeWaypoint(JsonObject freight, bool first)
        {
            var waypoints = Items(freight["Waypoints"]).ToList();
            var ordered = waypoints.OrderBy(w => Number(w["OrderNumber"]) ?? 0);
            return (first ? ordered.FirstOrDefault() : ordered.LastOrDefault())?.DeepClone();
        }

        private static JsonNode? OrderRevenue(JsonObject order)
        {
            var revenue = new JsonArray();
            void Add(string? description, double? amount) =>
                revenue.Add(new JsonObject { ["Description"] = description, ["Revenue"] = Money(amount) });

            var fixedPrice = Number(order["FixedPrice"]);
            var freightPrice = Sum(order["FreightItems"], "FreightPrice") ?? 0;
            if (fixedPrice is { } fixedValue && fixedValue != 0) Add("Vast bedrag", fixedValue);
            if (freightPrice != 0) Add("Vrachtprijs", freightPrice);
            foreach (var service in Items(order["AdditionalServices"])) Add(Text(service["Description"]), Number(service["Price"]));

            var price = fixedPrice is { } f && f != 0 ? f : freightPrice;
            var running = price;
            var discounts = Items(order["DiscountAndSurcharges"]).Where(d => !Flag(d["ValueIsPricePerKilometer"])).ToList();
            foreach (var discount in discounts.Where(d => !Flag(d["ApplyAfterOtherDiscountAndSurcharges"])))
            {
                var value = Number(discount["Value"]) ?? 0;
                var amount = Flag(discount["ValueIsPercentage"]) ? Math.Round(price / 100 * value, 2) : value;
                Add(Text(discount["Name"]), amount);
                running += amount;
            }
            foreach (var discount in discounts.Where(d => Flag(d["ApplyAfterOtherDiscountAndSurcharges"])))
            {
                var value = Number(discount["Value"]) ?? 0;
                Add(Text(discount["Name"]), Flag(discount["ValueIsPercentage"]) ? Math.Round(running / 100 * value, 2) : value);
            }
            return revenue;
        }

        private static JsonNode? FreightGroups(JsonObject order)
        {
            var freight = Items(order["FreightItems"]).ToList();
            if (freight.Any(item => Items(item["Waypoints"]).Count() != 2)) return null;

            var groups = new JsonArray();
            foreach (var group in freight.GroupBy(item => string.Join("|",
                         item["OriginWaypoint"]?["Relation"]?["Id"]?.ToJsonString(), Text(item["OriginWaypoint"]?["Date"]),
                         item["DestinationWaypoint"]?["Relation"]?["Id"]?.ToJsonString(), Text(item["DestinationWaypoint"]?["Date"]))))
            {
                var first = group.First();
                var goods = new JsonArray();
                foreach (var item in group)
                {
                    var good = item.DeepClone().AsObject();
                    good.Remove("Waypoints");
                    good.Remove("OriginWaypoint");
                    good.Remove("DestinationWaypoint");
                    goods.Add(good);
                }
                groups.Add(new JsonObject
                {
                    ["Waypoints"] = first["Waypoints"]?.DeepClone(),
                    ["Goods"] = goods,
                    ["OriginWaypoint"] = first["OriginWaypoint"]?.DeepClone(),
                    ["DestinationWaypoint"] = first["DestinationWaypoint"]?.DeepClone()
                });
            }
            return groups;
        }

        private static double InvoiceTotal(JsonObject invoice) => Sum(invoice["InvoiceLines"], "Price") ?? 0;

        private static double InvoiceVat(JsonObject invoice) =>
            Number(invoice["Vat"]?["Percentage"]) is { } percentage ? Math.Round(InvoiceTotal(invoice) * percentage / 100, 2) : 0;

        private static double InvoiceNet(JsonObject invoice) =>
            Number(invoice["Vat"]?["Percentage"]) is not null
                ? Math.Round(InvoiceTotal(invoice) + InvoiceVat(invoice), 2)
                : InvoiceTotal(invoice);

        private static JsonNode? DueDate(JsonObject invoice) =>
            Text(invoice["InvoiceDate"]) is { } date
                ? JsonValue.Create(System.DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                    .AddDays(Number(invoice["PaymentTerm"]) ?? 0).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                : null;

        private static JsonNode? WeekNumbers(JsonObject invoice)
        {
            var lines = Items(invoice["InvoiceLines"]).ToList();
            if (lines.Count == 0) return null;
            var weeks = lines
                .Select(line => Text(line["TransportOrderFreight"]?["OriginWaypoint"]?["Date"]))
                .Where(date => date is not null)
                .Select(date => ISOWeek.GetWeekOfYear(System.DateTime.ParseExact(date!, "yyyy-MM-dd", CultureInfo.InvariantCulture)))
                .Distinct()
                .OrderBy(week => week);
            return JsonValue.Create(string.Join(", ", weeks));
        }

        private static string? IdOf(JsonNode? entity) => entity?["Id"]?.ToJsonString();

        private static bool StopIsOrigin(JsonObject stop) =>
            Items(stop["Legs"]).Any(leg => IdOf(leg["OriginRelation"]) == IdOf(stop["Relation"])
                && IdOf(stop["WaypointType"]) == IdOf(leg["OriginWaypointType"]));

        private static List<JsonObject> StopLegs(JsonObject stop, out bool origin)
        {
            origin = StopIsOrigin(stop);
            var side = origin ? "Origin" : "Destination";
            return Items(stop["Legs"])
                .Where(leg => IdOf(leg[side + "Relation"]) == IdOf(stop["Relation"])
                    && IdOf(leg[side + "WaypointType"]) == IdOf(stop["WaypointType"]))
                .ToList();
        }

        private static JsonNode? StopFreight(JsonObject stop)
        {
            var freight = new JsonArray();
            foreach (var item in Items(stop["Legs"]).Select(leg => leg["TransportOrderFreight"]).OfType<JsonObject>()
                         .GroupBy(item => IdOf(item)).Select(group => group.First()))
            {
                freight.Add(item.DeepClone());
            }
            return freight;
        }

        private static JsonNode? StopJoin(JsonObject stop, string originMember, string destinationMember, string separator)
        {
            if (stop["Relation"] is null || !Items(stop["Legs"]).Any()) return null;
            var legs = StopLegs(stop, out var origin);
            var values = legs.Select(leg => Text(leg[origin ? originMember : destinationMember])).Where(v => v is not null).Distinct();
            return JsonValue.Create(string.Join(separator, values).Trim());
        }

        private static JsonNode? StopDate(JsonObject stop)
        {
            if (stop["Relation"] is null || !Items(stop["Legs"]).Any()) return null;
            var legs = StopLegs(stop, out var origin);
            var dates = legs.Select(leg => Text(leg[origin ? "DepartureFromOrigin" : "ArrivalAtDestination"]))
                .Where(date => date is not null).OrderBy(date => date, StringComparer.Ordinal).ToList();
            return dates.Count > 0 ? JsonValue.Create(dates[0]) : null;
        }

        private static JsonNode? StopTimeSlots(JsonObject stop)
        {
            if (stop["Relation"] is null || !Items(stop["Legs"]).Any()) return null;
            var relation = IdOf(stop["Relation"]);
            var legs = Items(stop["Legs"]).ToList();
            var slots = legs.Where(leg => IdOf(leg["OriginRelation"]) == relation).Select(leg => leg["OriginTimeSlot"]).OfType<JsonObject>().ToList();
            if (slots.Count == 0)
            {
                slots = legs.Where(leg => IdOf(leg["DestinationRelation"]) == relation).Select(leg => leg["DestinationTimeSlot"]).OfType<JsonObject>().ToList();
            }
            var names = slots.GroupBy(slot => IdOf(slot)).Select(group => Text(group.First()["Name"]));
            return JsonValue.Create(string.Join(' ', names).Trim());
        }

        private static IEnumerable<JsonObject> OutsourcingInvoices(JsonObject file) =>
            Items(file["IncomingInvoices"]).Where(invoice => Flag(invoice["IncomingInvoiceType"]?["IsOutsourcingInvoiceType"]));

        private static JsonNode? DriverNames(JsonObject file)
        {
            var names = Items(file["Drivers"]).Select(driver => Text(driver["Name"])).Distinct().ToList();
            return names.Count == 0 ? null : JsonValue.Create(string.Join(", ", names));
        }

        private static JsonNode? FileSummary(JsonObject file)
        {
            var totals = new List<(string Name, double Value)>();
            var freight = Items(file["Stops"]).SelectMany(stop => Items(stop["Freight"]))
                .GroupBy(item => IdOf(item)).Select(group => group.First());
            foreach (var item in freight)
            {
                var name = Text(item["Product"]?["Name"]);
                if (string.IsNullOrWhiteSpace(name)) continue;
                var description = Text(item["Product"]?["Description"]);
                var label = string.IsNullOrWhiteSpace(description) ? name.Trim() : description.Trim() + " " + name.Trim();
                var index = totals.FindIndex(t => string.Equals(t.Name, label, StringComparison.OrdinalIgnoreCase));
                var amount = Number(item["Amount"]) ?? 0;
                if (index < 0) totals.Add((label, amount));
                else totals[index] = (totals[index].Name, totals[index].Value + amount);
            }

            var productTotals = new JsonArray();
            foreach (var (name, value) in totals) productTotals.Add(new JsonObject { ["Name"] = name, ["Value"] = value });
            return new JsonObject { ["ProductTotals"] = productTotals };
        }

        private static JsonNode? PurchaseTotal(JsonObject order) =>
            Money(Items(order["OrderLines"]).Sum(line => Number(line["Price"]) ?? Number(line["IncomingInvoice"]?["ExpectedPrice"]) ?? 0));
    }
}
