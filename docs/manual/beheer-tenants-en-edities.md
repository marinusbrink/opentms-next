# Beheer — Tenants en edities

> **Vertaling:** deze pagina is de bronversie (Nederlands). De Engelse vertaling
> wordt gegenereerd als onderdeel van de documentatiestap na deze draft.

---

## Wat kun je hier doen?

Met de app **Tenants** beheer je als host de organisaties die OpenTMS
gebruiken (tenants) en de pakketten waarop ze zitten (edities). De app is er alleen
voor de host: wie bij een tenant is aangemeld, ziet hem niet in het app-overzicht. Het
navigatiepaneel links heeft twee onderdelen:

- **Tenants**: een tenant aanmaken, wijzigen of verwijderen. Elke tenant krijgt een eigen
  database.
- **Edities**: een editie aanmaken, een naam geven en per editie de functies (features)
  aan- of uitzetten. Een tenant op een editie krijgt die functies, tenzij voor de tenant
  zelf iets anders is ingesteld.

---

## Een tenant aanmaken

1. Open de app **Tenants** via het wafel-icoon (negen stippen) linksboven en kies links **Tenants**.
2. Klik op **Nieuwe tenant**.
3. Vul de **Naam** in en kies eventueel een **Editie**. Bestaat de editie nog niet, dan
   opent **Nieuwe editie toevoegen…** onderaan de keuzelijst het venster voor een nieuwe
   editie (met wat je in de keuzelijst typte als naam); na **Opslaan** is die editie
   gekozen. De functies van de nieuwe editie stel je daarna in bij **Edities**. Je hebt
   daarvoor het recht nodig om edities aan te maken.
4. Vul het **E-mailadres beheerder** in. Op dat adres ontvangt de beheerder van de tenant
   een e-mail met een link om zelf een wachtwoord te kiezen. Er wordt nooit een wachtwoord
   gemaild.
5. Onder **Database** zie je de connection string die de tenant krijgt. Die wordt
   gegenereerd en kun je niet wijzigen. Het wachtwoord is verborgen; de x'en in de naam
   worden bij het opslaan een deel van het tenant-id.
6. Klik op **Opslaan**.

De database wordt daarna op de achtergrond ingericht. In de lijst zie je de status:

| Status | Betekenis |
|---|---|
| **In wachtrij** | De inrichting staat klaar om te beginnen. |
| **Wordt ingericht** | De database, de tabellen en de beheerder worden aangemaakt. |
| **Gereed** | De tenant kan worden gebruikt; de uitnodiging is verstuurd. |
| **Mislukt** | Er ging iets mis. Open de tenant om de foutmelding te zien. |

De lijst ververst zichzelf zolang een tenant wordt ingericht.

<!-- SCREENSHOT: de app Tenants met het dialoogvenster Nieuwe tenant, met de gegenereerde connection string -->

### Is de inrichting mislukt?

Klik met de rechtermuisknop op de tenant en kies **Inrichting opnieuw proberen**. De
inrichting gaat verder waar ze is gestopt.

### Is de uitnodiging verlopen?

Klik met de rechtermuisknop op de tenant en kies **Uitnodiging opnieuw versturen**. De
beheerder krijgt een nieuwe link.

---

## Inloggen als een gebruiker van een tenant

Om een tenant te helpen kun je als host inloggen als een van zijn gebruikers.

1. Klik in de lijst **Tenants** met de rechtermuisknop op de tenant en kies **Inloggen als…**.
2. Kies de gebruiker en klik op **Inloggen als** (of dubbelklik op de gebruiker).
3. Je werkt nu in de omgeving van die tenant, als die gebruiker. Een gele balk bovenin laat zien
   als wie je bent ingelogd en namens wie.
4. Klik in die balk op **Terug naar host** om terug te gaan naar je eigen sessie.

Alles wat je doet terwijl je bent ingelogd als een ander, legt het logboek vast onder die
gebruiker én onder je eigen naam.

<!-- SCREENSHOT: de gele balk "U bent ingelogd als … namens …" met de knop Terug naar host -->

---

## Een tenant wijzigen

Dubbelklik op de tenant. Je kunt de **Naam** en de **Editie** wijzigen. De naam van de
database verandert niet als je de tenant een andere naam geeft.

---

## Een tenant verwijderen

> **Let op:** als je een tenant verwijdert, wordt ook zijn database met alle gegevens
> verwijderd. Dat kan niet ongedaan worden gemaakt.

1. Klik met de rechtermuisknop op de tenant en kies **Verwijderen**.
2. Typ ter bevestiging de naam van de tenant precies over.
3. Klik op **Verwijderen**.

De database wordt op de achtergrond verwijderd.

---

## Edities

1. Open de app **Tenants** via het wafel-icoon en kies links **Edities**.
2. Klik op **Nieuwe editie**, vul een **Naam** in en klik op **Opslaan**.
3. Open de editie opnieuw (dubbelklik) en ga naar het tabblad **Functies**. Zet de
   functies aan of uit en klik op **Opslaan**.

In de lijst zie je per editie hoeveel tenants erop zitten. Een editie met tenants kun je
niet verwijderen: zet die tenants eerst op een andere editie.

<!-- SCREENSHOT: de app Tenants, onderdeel Edities, met het tabblad Functies van een editie -->

---

## Functies van een editie

Elke app en elk onderdeel van het navigatiepaneel is een functie. Een onderdeel is alleen
zichtbaar als de functie zelf én alle functies erboven aan staan: zet je **App Stamgegevens** uit,
dan verdwijnen ook alle lijsten daaronder. Keuzelijsten in een scherm (bijvoorbeeld het land in
een relatie) werken zolang de app aan staat, ook als de lijst zelf uit staat. De host heeft de app
Beheer altijd; de app Tenants is geen functie.

| Functie | Technische naam | Valt onder | Standaard |
|---|---|---|---|
| App Dashboard | `Apps.Dashboard` | | uit |
| App Planbord | `Apps.Planboard` | | uit |
| App Transport | `Apps.Transport` | | uit |
| App Financieel | `Apps.Finance` | | uit |
| App Stamgegevens | `Apps.MasterData` | | aan |
| Groep Relaties | `Apps.MasterData.Relations` | App Stamgegevens | aan |
| Relaties | `Apps.MasterData.Relations.Relations` | Groep Relaties | uit |
| Adrestypen | `Apps.MasterData.Relations.AddressTypes` | Groep Relaties | uit |
| Relatiecategorieën | `Apps.MasterData.Relations.RelationCategories` | Groep Relaties | uit |
| Groep Transport | `Apps.MasterData.Transport` | App Stamgegevens | aan |
| Producten | `Apps.MasterData.Transport.Products` | Groep Transport | uit |
| Transporteenheden | `Apps.MasterData.Transport.TransportUnits` | Groep Transport | uit |
| Productrelatiecodes | `Apps.MasterData.Transport.ProductRelationCodes` | Groep Transport | uit |
| Temperaturen | `Apps.MasterData.Transport.Temperatures` | Groep Transport | uit |
| Groep Wagenpark | `Apps.MasterData.Fleet` | App Stamgegevens | aan |
| Voertuigen | `Apps.MasterData.Fleet.Vehicles` | Groep Wagenpark | uit |
| Voertuigcombinaties | `Apps.MasterData.Fleet.VehicleCombinations` | Groep Wagenpark | uit |
| Chauffeurs | `Apps.MasterData.Fleet.Drivers` | Groep Wagenpark | uit |
| Voertuigtypen | `Apps.MasterData.Fleet.VehicleTypes` | Groep Wagenpark | uit |
| Transportkenmerken | `Apps.MasterData.Fleet.TransportCharacteristics` | Groep Wagenpark | uit |
| Groep Regio's en lijsten | `Apps.MasterData.Reference` | App Stamgegevens | aan |
| Landen | `Apps.MasterData.Reference.Countries` | Groep Regio's en lijsten | uit |
| Valuta's | `Apps.MasterData.Reference.Currencies` | Groep Regio's en lijsten | uit |
| Geografische regio's | `Apps.MasterData.Reference.GeographicalRegions` | Groep Regio's en lijsten | uit |
| App Integraties | `Apps.Integrations` | | uit |
| App Rapporten | `Apps.Reports` | | uit |
| App Beheer | `Apps.Administration` | | aan |
| Gebruikers | `Apps.Administration.Users` | App Beheer | aan |
| Rollen | `Apps.Administration.Roles` | App Beheer | aan |
| Organisatie-eenheden | `Apps.Administration.OrganizationUnits` | App Beheer | aan |
| Groep Auditlogboeken | `Apps.Administration.AuditLogs` | App Beheer | aan |
| Functioneel logboek | `Apps.Administration.AuditLogs.FunctionalLogbook` | Groep Auditlogboeken | aan |
| Technisch logboek | `Apps.Administration.AuditLogs.TechnicalLogbook` | Groep Auditlogboeken | aan |
| Onderhoud | `Apps.Administration.Maintenance` | App Beheer | uit |
| Adres opzoeken op postcode | `Integrations.AddressLookup` | | uit |
| Instellingenbeheer inschakelen | `SettingManagement.Enable` | | aan |
| E-mailinstellingen mogen wijzigen | `SettingManagement.AllowChangingEmailSettings` | Instellingenbeheer inschakelen | uit |

---

## Rechten

| Recht | Technische naam | Wat het toestaat |
|---|---|---|
| **Tenants** | `Platform.Tenants` | De lijst met tenants bekijken. |
| **Tenants aanmaken** | `Platform.Tenants.Create` | Een tenant aanmaken en een mislukte inrichting opnieuw proberen. |
| **Tenants wijzigen** | `Platform.Tenants.Update` | Naam en editie wijzigen, een uitnodiging opnieuw versturen. |
| **Tenants verwijderen** | `Platform.Tenants.Delete` | Een tenant en zijn database verwijderen. |
| **Inloggen als tenantgebruiker** | `Platform.Tenants.Impersonation` | Inloggen als een gebruiker van een tenant. |
| **Edities** | `Platform.Editions` | De lijst met edities bekijken. |
| **Edities aanmaken** | `Platform.Editions.Create` | Een editie aanmaken. |
| **Edities wijzigen** | `Platform.Editions.Update` | De naam van een editie wijzigen. |
| **Edities verwijderen** | `Platform.Editions.Delete` | Een editie zonder tenants verwijderen. |
| **Functies van edities beheren** | `Platform.Editions.ManageFeatures` | Het tabblad Functies van een editie gebruiken. |

Deze rechten bestaan alleen aan de hostkant.
