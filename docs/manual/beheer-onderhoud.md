# Beheer — Onderhoud

> **Vertaling:** deze pagina is de bronversie (Nederlands). De Engelse vertaling
> wordt gegenereerd als onderdeel van de documentatiestap na deze draft.

---

## Wat kun je hier doen?

De weergave **Onderhoud** in de **Beheer**-app laat zien hoe het systeem er technisch
voor staat, en biedt een paar beheerhandelingen die normaal gesproken niet nodig
zijn. De weergave is bedoeld voor beheerders en support.

De weergave bestaat uit tabbladen. Welke tabbladen je ziet, hangt af van je rechten
en van waar je bent aangemeld:

| Tabblad | Host | Tenant | Wat je ziet |
|---|---|---|---|
| **Caches** | ja | nee | De applicatiecache van de instantie en de knop **Caches legen**. |
| **Databases** | ja | nee | Alle databases (de hostdatabase en de database van elke tenant) met grootte en statistieken. |
| **Outbox** | ja | ja | De wachtende events per database en wat de verzender het laatst heeft gedaan. Een tenant ziet alleen zijn eigen database. |
| **Gezondheid** | ja | nee | Wat er mis is met welk onderdeel en wat je eraan doet, plus het instantieregister. |
| **Achtergrondtaken** | ja | nee | De achtergrondtaken (Hangfire): aantallen per status, servers, wachtrijen en de taken zelf. |

Een tabblad waarvoor je geen rechten hebt, zie je niet. Voor tenants staat de hele
weergave achter de functie (feature) **Onderhoud** van de app Beheer, die aan staat via de
editie van de tenant (zie [Tenants en edities](beheer-tenants-en-edities.md)). De host heeft de
weergave altijd.

---

## De weergave openen

1. Open de **Beheer**-app via het wafel-icoon (negen stippen) rechts bovenin.
2. Klik in het navigatiepaneel links op **Onderhoud**.
3. Kies bovenin een tabblad.

<!-- SCREENSHOT: de Beheer-app met het navigatiepaneel en de weergave Onderhoud, tabblad Caches actief -->

---

## Caches

De applicatie onthoudt veelgebruikte gegevens (rechten, instellingen, featurewaarden)
in een cache, zodat die niet bij elk verzoek uit de database hoeven te komen.

Bovenaan staan de kerncijfers:

| Kaart | Betekenis |
|---|---|
| **Provider** | Waar de cache leeft. Nu: *in het geheugen, per instantie*. |
| **Items** | Het aantal items dat de cache op dit moment bevat. |
| **Caches** | Het aantal verschillende caches (elk soort gegeven heeft zijn eigen cache, bijvoorbeeld rechten of instellingen). |
| **Totale grootte** | De omvang van alle items samen. |
| **Instantie** | De naam van de machine die dit verzoek beantwoordde. |

Daaronder staat de lijst met alle items die de cache nu bevat:

| Kolom | Betekenis |
|---|---|
| **Cache** | De cache waartoe het item behoort, bijvoorbeeld *PermissionGrantCacheItem* (rechten) of *SettingCacheItem* (instellingen). |
| **Sleutel** | De sleutel van het item binnen zijn cache. |
| **Grootte** | De omvang van het item. |
| **Opgeslagen** | Wanneer het item in de cache is gezet. |
| **Verloopt** | Wanneer het item vanzelf uit de cache gaat (leeg als het geen vaste vervaltijd heeft). |

Met het filter **Cache** in de werkbalk toon je alleen de items van één cache; achter elke
naam staat het aantal items. Via **Kolommen** kun je ook de glijdende vervaltijd en de
volledige sleutel tonen.

**Item verwijderen** (rechtermuisknop op een regel) haalt één item uit de cache. Het wordt
bij het volgende gebruik opnieuw uit de database gelezen.

**Caches legen** verwijdert alle items in één keer. Gebruik dit nadat er rechtstreeks in
de database iets is aangepast aan rechten, instellingen of features en de wijziging
niet zichtbaar wordt. Je bevestigt de handeling eerst. Na afloop zie je een melding met
het aantal verwijderde items.

> **Let op:** zolang de cache in het geheugen van één instantie zit, leeg je alleen de
> instantie die jouw verzoek beantwoordt. Draaien er meer instanties, dan houden de
> andere hun items tot die vanzelf verlopen. De waarschuwing onder de gegevens herinnert
> je hieraan.

<!-- SCREENSHOT: tabblad Caches met de kerncijfers, de waarschuwing, het filter Cache en de lijst met items -->

---

## Databases

Bovenaan staan de kerncijfers: het aantal databases, het aantal tenantdatabases, het
aantal gedeelde tenants (tenants zonder eigen database, die in de hostdatabase leven),
de totale grootte en de blobopslag van de hostdatabase (documenten en afbeeldingen die
in de database staan).

Daaronder staat de lijst met één regel per database.

| Kolom | Betekenis |
|---|---|
| **Database** | De naam van de database op de server. |
| **Rol** | *Host* of *Tenant*. |
| **Tenant** | De tenant waarvan dit de database is (leeg voor de host). |
| **Server** | De server (adres en poort) waarop de database staat. |
| **Grootte** | De grootte van de database op schijf. |
| **Verbindingen** | Het aantal open verbindingen naar deze database. |
| **Cache-hit** | Het deel van de leesacties dat uit het geheugen van de databaseserver kwam. Lager dan ongeveer 95% kan wijzen op te weinig geheugen. |
| **Commits** en **Rollbacks** | Het aantal afgeronde en teruggedraaide transacties sinds de laatste reset van de statistieken. |
| **Deadlocks** | Het aantal keren dat twee transacties op elkaar wachtten. Hoort nul of bijna nul te zijn. |

Via **Kolommen** kun je ook de aantallen gelezen, ingevoegde, gewijzigde en verwijderde
rijen, de tijdelijke bestanden, het moment van de laatste statistiekreset, het aantal
gedeelde tenants en de bereikbaarheid tonen. Een database die niet bereikbaar is, staat
in de lijst zonder cijfers.

### Een database bekijken

Dubbelklik op een regel (of druk op Enter) om de database te openen. Je ziet dan:

- **Algemeen:** naam, server, rol, tenant, grootte, bij de host de gedeelde tenants, en de
  **blobopslag** in deze database (aantal blobs, inhoud, tabelgrootte).
- **Schema's:** elk schema met zijn grootte en aantal tabellen. Elke module heeft zijn
  eigen schema (`orders`, `planning`, `financial`, enzovoort).
- **Grootste tabellen:** de twintig grootste tabellen met grootte, actieve en dode rijen,
  sequentiële scans en indexscans, en de laatste vacuum en analyze. Veel dode rijen of
  veel sequentiële scans op een grote tabel zijn signalen voor onderhoud.

<!-- SCREENSHOT: het dialoogvenster van een database met het tabblad Schema's -->

---

## Outbox

Elke wijziging die andere onderdelen van OpenTMS moeten weten, wordt als *event* in de
outbox van de database geschreven en daarna door de verzender gepubliceerd. Dit tabblad
laat zien of dat soepel verloopt.

Bovenaan staan de instellingen van de verzender op deze instantie (aan of uit, het
sweep-interval, de batchgrootte) en de naam van de instantie.

| Kolom | Betekenis |
|---|---|
| **Database** | De database waarvan dit de outbox is. |
| **Rol** en **Tenant** | Host of tenant, en welke tenant. |
| **Wachtend** | Het aantal events dat nog verzonden moet worden. Normaal nul of bijna nul. |
| **Oudste wachtend** | Het tijdstip van het oudste wachtende event. Een oud tijdstip betekent dat de verzender vastloopt. |
| **Laatste run** en **Laatst verzonden** | Wanneer de verzender deze outbox het laatst bezocht en hoeveel events het toen heeft gepubliceerd. |
| **Status** | De laatste fout van de verzender, of de melding dat de database niet bereikbaar is. |

> Laatste run, laatst verzonden en de laatste fout worden onthouden door de instantie die
> jouw verzoek beantwoordt, sinds die is gestart. De waarschuwing boven de lijst noemt de
> instantie.

**Nu verzenden** (rechtermuisknop op een regel) laat de verzender deze outbox direct
bezoeken. Gebruik dit als er events blijven wachten. Je ziet daarna een melding met het
aantal gepubliceerde events. Blijft een event hangen, dan staat de reden in de kolom
**Status**.

**Als tenant** zie je alleen je eigen database. Leeft je tenant in de gedeelde
hostdatabase, dan zie je dat de host deze outbox verzendt; de cijfers en de knop zijn
dan alleen voor de host beschikbaar.

<!-- SCREENSHOT: tabblad Outbox met de kerncijfers en de lijst -->

---

## Gezondheid

Dit tabblad vertelt je of OpenTMS goed draait, en zo niet: **welk onderdeel**, **wat daardoor niet werkt** en **wat
je eraan doet**.

### De kaarten

- **Status**: de gezondheid van de instantie die jouw verzoek beantwoordt: **Gezond**, **Verminderd** of **Ongezond**.
- **Onderdelen**: hoeveel onderdelen aandacht vragen ("2 van 11 onderdelen vragen aandacht"). Staat de status niet op
  Gezond, dan staat hier waarom.
- Daarnaast het aantal actieve instanties, het aantal dat niet reageert, het aantal gestopte en het heartbeat-interval.

### Onderdelen

De bovenste lijst toont elk onderdeel waar OpenTMS van afhangt, de problemen bovenaan. De controles draaien live op de
instantie die jouw verzoek beantwoordt, bij het openen van het tabblad en bij **Vernieuwen**.

| Onderdeel | Soort | Wat er gecontroleerd wordt |
|---|---|---|
| **Hostdatabase** | Systeem | Of de hostdatabase antwoordt. Zonder hostdatabase werkt niets. |
| **Tenantdatabases** | Systeem | Of elke tenant met een eigen database die database kan bereiken. Noemt de tenants die niet antwoorden. |
| **Achtergrondtaken** | Systeem | Of er een taakserver draait en of er taken zijn mislukt na al hun herhalingen. |
| **Outbox (gebeurtenissen)** | Systeem | Of een instantie gebeurtenissen aflevert, of de laatste aflevering lukte en of er gebeurtenissen te lang wachten. |
| **Cache** | Systeem, optioneel | Of meerdere instanties elk een eigen cache hebben (dan bereiken wijzigingen de andere instanties later). |
| **Live-updates** | Systeem, optioneel | Of schermen live verversen over instanties heen (Redis-backplane). |
| **Documenten (pdfgeneratorapi)** | Externe dienst | Of documenten gemaakt kunnen worden. |
| **Routering** | Externe dienst | Of de routeringsserver antwoordt (afstanden, rijtijden, routes). |
| **Routefunctie (CO₂)** | Externe dienst, optioneel | Of de routefunctie voor CO₂ antwoordt; zonder valt CO₂ terug op een schatting. |
| **Stopvolgorde (VROOM)** | Externe dienst, optioneel | Of VROOM antwoordt; zonder bepaalt een ingebouwde heuristiek de stopvolgorde. |
| **E-mail (Postmark)** | Externe dienst | Of Postmark het ingestelde servertoken accepteert. |

Per onderdeel zie je de **Status**, de **Bevinding** (wat er aan de hand is en wat je doet) en **Wat ervan afhangt**.
Een onderdeel dat **optioneel** is, maakt de status niet slechter zolang het niet is ingesteld: OpenTMS gebruikt dan een
terugvaloptie.

**Een onderdeel bekijken:** dubbelklik op de regel (of druk op Enter). Je ziet de bevinding met wat je moet doen, wat
ervan afhangt, de **instellingen** die het onderdeel bepalen (alleen de namen, nooit de waarden, met de bijbehorende
omgevingsvariabele) en de technische uitslag van de controle met de foutmelding. De externe diensten stel je in bij
**Beheer → Diensten** (zie [Diensten](beheer-diensten.md)).

### Instanties

De onderste lijst is het **instantieregister**: elke draaiende (of onlangs gestopte) instantie van OpenTMS, van elke
rol. Een instantie meldt zich bij de start aan, stuurt elke halve minuut een heartbeat met de uitslag van haar
gezondheidscontroles, en meldt zich af bij een nette afsluiting. Wordt een instantie door het platform hard beëindigd
(bij automatisch schalen kan dat), dan valt de heartbeat stil en toont het register haar na anderhalve minuut als
**Reageert niet**.

| Kolom | Betekenis |
|---|---|
| **Status** | **Actief**, **Reageert niet** of **Gestopt**. |
| **Rol** | De rol van het proces: nu altijd *Api*; later ook *Jobs*, *Ingest* en *Agents*. |
| **Machine** | De machine of container waarop de instantie draait. |
| **Deze instantie** | Een vinkje bij de instantie die jouw verzoek beantwoordde. |
| **Gezondheid** | De uitslag van de laatste gezondheidscontrole van die instantie. |
| **Versie** | De softwareversie die de instantie draait. Handig om een uitrol te controleren. |
| **Gestart**, **Laatste heartbeat**, **Gestopt** | De levensloop van de instantie. |
| **Uptime** | Hoe lang de instantie draait of heeft gedraaid. |

Via **Kolommen** kun je ook de omgeving, het werkgeheugen, het managed geheugen, de outbox-verzender en het proces-id
tonen. Gestopte en stilgevallen instanties blijven een week in de lijst staan en verdwijnen daarna vanzelf.

**Een instantie bekijken:** dubbelklik op een regel. Je ziet onder **Algemeen** de levensloop en het proces (versie,
omgeving, runtime, geheugen, processors, outbox-verzender) en onder **Gezondheid** de onderdelen van haar laatste
heartbeat, met hun bevinding.

<!-- SCREENSHOT: tabblad Gezondheid met de kaarten, de onderdelen en het instantieregister -->

---

## Achtergrondtaken

Werk dat niet in een verzoek past (zoals het aanmaken van een tenant) draait als
achtergrondtaak. Dit tabblad toont de taakmotor (Hangfire).

- De kaarten bovenaan tonen het aantal taken per status. **Klik op een kaart** om de
  taken van die status in de lijst te tonen. Daarnaast zie je het aantal servers en de
  wachtrijen met hun lengte.
- Het filter **Status** in de werkbalk doet hetzelfde.

| Status | Betekenis |
|---|---|
| **In wachtrij** | Wacht tot een worker vrij is. |
| **Gepland** | Draait op een later moment (zie **Volgende run**). |
| **In uitvoering** | Draait nu. |
| **Geslaagd** | Afgerond zonder fout. |
| **Mislukt** | Afgebroken met een fout; de reden en de fout staan in de lijst. |
| **Verwijderd** | Wordt niet (meer) uitgevoerd. |
| **Terugkerend** | Een schema dat herhaaldelijk een taak start (zie **Schema** en **Volgende run**). |

Via de rechtermuisknop op een taak kun je, als je de rechten hebt:

- **Opnieuw in wachtrij:** een mislukte, verwijderde of geplande taak direct opnieuw
  laten draaien.
- **Verwijderen:** een taak die nog moet draaien afbreken. Dit kan niet ongedaan worden
  gemaakt.

Beide handelingen bevestig je eerst.

> Achtergrondtaken zijn alleen voor de host zichtbaar: de taakopslag is één gedeelde
> opslag zonder tenantscheiding. Taken die voor een tenant draaien, tonen die tenant in de
> kolom **Tenant** (via **Kolommen**).

<!-- SCREENSHOT: tabblad Achtergrondtaken met de kaarten per status en de lijst met mislukte taken -->
