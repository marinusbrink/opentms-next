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
| **Gezondheid** | ja | nee | De gezondheidscontroles en gegevens over het proces. |
| **Achtergrondtaken** | ja | nee | De achtergrondtaken (Hangfire): aantallen per status, servers, wachtrijen en de taken zelf. |

Een tabblad waarvoor je geen rechten hebt, zie je niet. Voor tenants staat de hele
weergave achter de instelling (feature) **Onderhoudsweergave**, die de host per tenant
aanzet.

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

Je ziet:

| Veld | Betekenis |
|---|---|
| **Provider** | Waar de cache leeft. Nu: *in het geheugen, per instantie*. |
| **Items** | Het aantal items dat de cache op dit moment bevat. |
| **Sleutelvoorvoegsel** | Het voorvoegsel dat voor elke cachesleutel wordt gebruikt (meestal leeg). |
| **Instantie** | De naam van de machine die dit verzoek beantwoordde. |

**Caches legen** verwijdert alle items in één keer. Gebruik dit nadat er rechtstreeks in
de database iets is aangepast aan rechten, instellingen of features en de wijziging
niet zichtbaar wordt. Je bevestigt de handeling eerst. Na afloop zie je een melding met
het aantal verwijderde items.

> **Let op:** zolang de cache in het geheugen van één instantie zit, leeg je alleen de
> instantie die jouw verzoek beantwoordt. Draaien er meer instanties, dan houden de
> andere hun items tot die vanzelf verlopen. De waarschuwing onder de gegevens herinnert
> je hieraan.

<!-- SCREENSHOT: tabblad Caches met de gegevens, de waarschuwing en de knop Caches legen -->

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

Dit tabblad voert dezelfde controles uit als de gezondheidsadressen van de server
(`/health/ready`) en toont het resultaat.

- De kaarten bovenaan: de totale status (**Gezond**, **Verminderd** of **Ongezond**), de
  duur, het aantal controles en de uptime van het proces.
- **Controles:** elke controle met status, duur, tags en, als die er is, de fout.
- **Proces:** applicatie, versie, omgeving, machine, starttijd, runtime, geheugen, aantal
  processors en of de outbox-verzender op deze instantie aanstaat.

Met **Vernieuwen** voer je de controles opnieuw uit.

<!-- SCREENSHOT: tabblad Gezondheid met de kaarten, de controles en het proces -->

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
