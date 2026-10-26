# Transport

De app **Transport** is de werkplek van de orderafdeling en de planning: hier legt u transportorders
vast, ziet u de prijzen die het systeem berekent, vraagt u een rit aan, drukt u vrachtbrieven (CMR) af
en volgt u de ritten waarop uw orders terechtkomen. Het navigatiepaneel heeft vier onderdelen:

| Onderdeel | Wat u er vindt |
|---|---|
| **Opdrachten** | De transportorders, met de vaste weergaven *Vandaag*, *Morgen*, *Deze week*, *Open*, *Concepten* en *Alles* |
| **Sjablonen** | Terugkerende orders: een order die het systeem volgens een schema aanmaakt |
| **Ritten** | De transportritten, met de weergaven *Vandaag*, *Deze week*, *Open* en *Alles* |
| **Documenten** | Alle afgedrukte vrachtbrieven van uw organisatie |

Elk onderdeel is per tenant in- of uit te schakelen via de editie (de functies *Transport*,
*Opdrachten*, *Sjablonen*, *Ritten* en *Documenten*). Staat *Opdrachten* uit, dan staat *Sjablonen*
ook uit. Wat een gebruiker mag, bepalen de rechten: *Transportorders bekijken*, *aanmaken*,
*wijzigen*, *verwijderen*, *bevestigen*, *annuleren*, *goedkeuren*, *herzien*, *archiveren* en
*vrachtbrieven afdrukken*; voor sjablonen *bekijken*, *aanmaken*, *wijzigen* en *verwijderen*; voor
ritten *bekijken*, *wijzigen*, *starten*, *afronden*, *annuleren*, *archiveren*, *legs toewijzen* en
*stops beheren*; *Legs wijzigen*; en voor de prijzen *Tariefregels wijzigen*. Een knop waarvoor u het
recht niet hebt, ziet u niet.

Een order of rit opent als eigen pagina met een eigen adres (bijvoorbeeld `/transport/orders/…`). U kunt
dat adres bewaren of doorsturen; met **Sluiten** keert u terug naar de lijst, met de filters zoals u ze
had. Hebt u nog niet-opgeslagen wijzigingen, dan vraagt het systeem eerst of u die wilt weggooien.

Elke keuzelijst in een order, sjabloon, rit of wizard die een record kiest (klant, relatie, adres,
contactpersoon, ordertype, stoptype, tijdslot, transporteenheid, product, voertuig, chauffeur,
prijslijst, vrachtbriefprofiel, sjabloon en zo verder) eindigt met **Nieuw … toevoegen…**: het scherm
voor het nieuwe record opent bovenop de order en na het opslaan is het gekozen. Zie *Nieuw record vanuit
een keuzelijst* bij Stamgegevens. Alleen de regel van een tariefregel (de regels van de order zelf) en de
leg van een kostenregel (die volgen uit de orders) hebben de regel niet: daar valt niets nieuws te maken.

## Berekende waarden

Opslaan gaat direct: het systeem berekent prijzen, afstanden, CO₂, kosten, marge en voortgang daarna
op de achtergrond en toont de nieuwe waarden vanzelf zodra ze klaar zijn. Zolang een berekening loopt,
staat er een draaiend symbooltje met *Berekenen…* naast de waarde en is de oude waarde grijs. Mislukt
een berekening (bijvoorbeeld omdat de routeplanner niet bereikbaar is), dan ziet u een waarschuwing; de
berekening wordt automatisch opnieuw geprobeerd en tot die tijd blijven de vorige waarden staan.

## De lijst Opdrachten

- Kies links in het navigatiepaneel de weergave. *Vandaag*, *Morgen* en *Deze week* tonen de orders van
  die dagen, *Open* alle bevestigde orders die nog niet gefactureerd zijn, *Concepten* de orders die nog
  niet bevestigd zijn. Alleen bij *Alles* kiest u zelf een periode (van en tot).
- Boven de lijst filtert u verder op status, klant en organisatie-eenheid; het zoekveld zoekt op
  ordernummer, referentie, klant en de namen en plaatsen van de eerste en laatste stop.
- De kolommen *Opbrengst*, *Kosten*, *Marge* en *CO₂* zijn berekende waarden (zie hierboven).
  Via *Kolommen* zet u verborgen kolommen aan, zoals het ordertype en de organisatie-eenheid.
- Dubbelklik op een regel om de order te openen.
- In de actiebalk: **Nieuwe transportorder** (heeft u meerdere ordertypen, dan staat er per type een
  knop), **Nieuw van sjabloon**, en voor de geselecteerde order(s) **Order kopiëren**, **Vrachtbrief
  afdrukken**, **Goedkeuren**, **Order annuleren** en **Verwijderen**. Rechtsklikken op een regel geeft
  dezelfde acties.

## Een order aanmaken

**Nieuwe transportorder** opent een klein venster met het ordertype, de klant, de orderdatum (standaard
vandaag) en de klantreferentie. Na **Opslaan** staat de order als *Concept* klaar en opent de pagina van de
order. De standaardstops van het ordertype worden het *stoppatroon* van de order en de financiële
standaarden van de klant (valuta, btw, betaaltermijn, prijslijsten, kortingen en toeslagen) de
financiële voorwaarden.

Een concept mag onvolledig zijn. Pas bij **Bevestigen** controleert het systeem of alles ingevuld is wat
het ordertype verplicht stelt (de velden met een sterretje), of er minstens één regel is en of elke
regel minstens twee stops heeft. Ontbreekt er iets, dan staat bovenaan de pagina wat er mist. Bij het
bevestigen krijgt de order haar nummer en maakt de planning de legs (trajecten) aan.

## De orderpagina

Bovenaan staan het nummer (of *Concept*) en de status. De knoppen hangen af van de status:

| Status | Knoppen |
|---|---|
| Concept | Opslaan, Bevestigen, Order annuleren, Vrachtbrief afdrukken, Verwijderen |
| Open | Opslaan, Goedkeuren, Order annuleren, Vrachtbrief afdrukken, Verwijderen |
| Gereed voor facturatie | Archiveren, Herzien, Vrachtbrief afdrukken |
| Gefactureerd | Archiveren, Vrachtbrief afdrukken |
| Gearchiveerd | Herzien, Vrachtbrief afdrukken |
| Geannuleerd | Vrachtbrief afdrukken |

Een order in de status *Gereed voor facturatie*, *Gefactureerd*, *Gearchiveerd* of *Geannuleerd* is
alleen te bekijken. Met **Opslaan als sjabloon** maakt u van de order een sjabloon (alleen met het recht
om sjablonen aan te maken). Drukt u een vrachtbrief af terwijl er nog wijzigingen openstaan, dan slaat het
systeem eerst op.

- **Goedkeuren** controleert de order tegen de goedkeuringsregels van uw organisatie en toont per regel
  het resultaat (waarschuwing of fout) en de status die de order krijgt: *Gereed voor facturatie*, of
  *Gearchiveerd* als er niets te factureren valt. Alleen orders zonder fout worden goedgekeurd. Wijzigt er
  iets tussen controleren en goedkeuren, dan vraagt het systeem u opnieuw te controleren.
- **Herzien** zet een order uit *Gereed voor facturatie* of *Gearchiveerd* terug naar *Open*. U moet een
  reden opgeven; die wordt bij de order bewaard. Een gefactureerde order kan niet herzien worden.
- **Order annuleren** kan zolang geen leg op een rit staat die onderweg of afgerond is.

### Tabblad Algemeen

Ordertype (vast zodra de order een nummer heeft), klant, contactpersoon, orderdatum, klantreferentie,
organisatie-eenheid, de interne en de factuuropmerkingen, en de *kenmerken* die uw organisatie voor
orders heeft gedefinieerd (in de app Stamgegevens, onder Orders). Onderaan staat het **stoppatroon**:
de stops waarmee elke nieuwe regel begint. Een gewijzigd patroon geldt alleen voor regels die u daarna
toevoegt.

Onder **Referenties** staan de referenties van de order (type, waarde en omschrijving), bijvoorbeeld een
inkoopordernummer van de klant. **Referentie toevoegen** voegt een regel toe; type en waarde zijn
verplicht (een type heeft hoogstens 32 tekens, een waarde 128, een omschrijving 256; hoogstens 50
referenties per order).

Kiest u bij een opgeslagen order een **andere klant**, dan vraagt het systeem *De financiële
standaardinstellingen van … overnemen?* Met **Overnemen** worden de valuta, btw, betaaltermijn,
prijslijsten en kortingen en toeslagen van de nieuwe klant de financiële voorwaarden van de order (zoals
bij een nieuwe order: de prijslijsten in hun volgorde en alleen de kortingen en toeslagen die op de
orderdatum gelden; een klant zonder financiële instellingen geeft euro zonder prijslijsten). Met
**Annuleren** blijven de huidige voorwaarden staan. In beide gevallen gelden ze zodra u de order opslaat;
op het tabblad Prijzen ziet u ze vooraf.

### Tabblad Regels

De regels (vrachten) staan in een tabel: transporteenheid, product, aantal, posities, colli, gewicht,
volume, laadmeters, europallets, en na het berekenen de afstand, de duur, de CO₂ en de prijs van de
regel. Aantallen en gewichten past u direct in de tabel aan. **Regel toevoegen** maakt een regel met de
stops van het stoppatroon (de datums volgen de orderdatum).

Klik op het potlood (of dubbelklik) om een regel te openen. Onder de tabel verschijnen dan de
onderdelen *Lading*, *Container*, *Teksten en documenten*, *Afmetingen*, *Kenmerken* en de **stops**
van die regel. Onder *Afmetingen* staan naast lengte, breedte en hoogte drie **wegingen** (kg) en drie
**volumes** (m³) per regel, bijvoorbeeld de gewogen gewichten per pallet; een leeg veld telt niet mee. De stops past u in hun eigen tabel aan: stoptype, datum, tijdslot of tijdvenster
(van–tot, als UU:MM), referentie en opmerking. Een stop openen geeft de relatie, het adres van die
relatie, de contactpersoon en de geplande duur; met de pijltjes verschuift u de stop. Velden die het
ordertype verplicht stelt, hebben een sterretje in de kolomkop.

### Tabblad Prijzen

Hier staan de **tariefregels** van de order zoals het systeem ze heeft berekend en opgeslagen: eerst per
regel, daarna de regels op orderniveau (kortingen en toeslagen, extra diensten, vaste prijs). Het soort
regel staat als label in de tabel; houdt u de muis erboven, dan ziet u waar de prijs vandaan komt
(prijslijst, regel, kolom, hoeveelheid). Met het recht *Tariefregels wijzigen* kunt u:

- **Regel toevoegen**: een handmatige regel (omschrijving, aantal, eenheidsprijs), op een regel of op de
  order;
- **Extra dienst toevoegen**: een extra dienst uit de stamgegevens;
- de **eenheidsprijs** van een berekende regel aanpassen (in de tabel);
- een handmatige regel **verwijderen** (prullenbak);
- een **vaste prijs instellen** of weer verwijderen.

Onder de tabel staan de totalen (regels, kortingen en toeslagen, extra diensten, totaal), met de valuta
één keer erbij. Past u op het tabblad Regels iets aan zonder op te slaan, dan toont het tabblad Prijzen
na een ogenblik een **voorbeeld** van de nieuwe prijzen, met een melding dat het om een voorbeeld gaat.
Na **Opslaan** komen de opgeslagen, opnieuw berekende prijzen terug.

Onderaan staan de **financiële voorwaarden**: valuta, btw, betaaltermijn, prijslijsten, wat er gebeurt
als meerdere prijslijsten passen, kortingen en toeslagen (die van de klant zijn gemarkeerd als
*standaard klant*), een vaste prijs en of de vrachtdocumenten met de factuur meegaan.

### Tabblad Planning

Bovenaan de **legs** van de order: elk traject tussen twee stops, met de geplande datums, afstand, duur,
tolkilometers, CO₂, het rittnummer waarop de leg staat en de toegerekende kosten en opbrengst.
Dubbelklik op een leg op een rit om die rit te openen; bij een leg zonder rit wijzigt u de referenties,
de geplande datums (binnen het tijdvenster van de stop) en de opmerking. Legs bestaan pas na het
bevestigen.

Daaronder het onderdeel **Rit** ("Tevens rit"): zet het vinkje *Maak bij het bevestigen direct een rit aan
voor deze order* aan en vul het rittype, het vertrek en de aankomst (relatie en adres) en de geplande
vertrekdatum en -tijd in. Onder **Geavanceerd** kiest u desgewenst de vervoerder, de voertuigen of een
combinatie, de chauffeurs en een afgesproken inkoopprijs. Bij **Bevestigen** maakt het systeem de rit
één keer aan en zet alle legs van de order erop. Daarna is dit onderdeel alleen te bekijken en staat er
een knop naar de aangemaakte rit; latere wijzigingen aan de order veranderen de rit niet.

### Tabblad Documenten

De afgedrukte vrachtbrieven van deze order, met nummer, afdrukmoment, profiel en bestand. Met het
download-symbool haalt u de PDF op; met **Vrachtbrief afdrukken** maakt u een nieuwe.

## Vrachtbrief (CMR) afdrukken

**Vrachtbrief afdrukken** opent een stappenvenster:

1. **Profiel**: het standaardprofiel is al gekozen. U kunt een ander profiel kiezen en voor deze afdruk
   het aantal exemplaren, randen, QR-code en *groeperen per leg* aanpassen, en per vak (afzender,
   geadresseerde, plaats van aflevering, plaats van inontvangstneming, vervoerder, opvolgende vervoerder,
   opgemaakt te) bepalen waar de gegevens vandaan komen.
2. **Regels**: vink de regels aan die op de vrachtbrief komen. Bij een rit gaan alle orders van de rit mee.
3. **Uitvoer**: met **Afdrukken** maakt het systeem één vrachtbrief per order. Per order ziet u of het
   gelukt is; lukt het niet (bijvoorbeeld omdat er nog geen vrachtbriefsjabloon is gekozen), dan staat de
   reden erbij. Per gemaakte vrachtbrief kiest u **Downloaden** of **Afdrukken** (opent de PDF).

Elke afdruk krijgt een eigen nummer (CMR-jaar-volgnummer) en wordt bewaard; u vindt hem terug bij de
order, bij de rit en onder **Documenten**.

## Sjablonen

Een sjabloon is een order die het systeem volgens een schema aanmaakt. De lijst toont naam, ordertype,
klant, herhaling, dagen, de laatste aanmaakdatum, het aantal regels en of het sjabloon actief is; met
*Toon inactief* ziet u ook de inactieve. **Nieuw sjabloon** vraagt naam, ordertype en klant en opent dan
het sjabloon.

Een sjabloon heeft dezelfde tabbladen als een order, met deze verschillen: in plaats van een nummer heeft
het een **naam** en *Actief*, de stops hebben een **dagverschuiving** (aantal dagen na de orderdatum) in
plaats van een datum, en op het tabblad Algemeen staat het onderdeel **Planning** (het schema):
herhaling (geen, wekelijks of maandelijks), interval, de weekdagen of de dag van de maand, de horizon
(hoeveel dagen vooruit het systeem orders aanmaakt, 1 tot 60) en de geldigheid (van en tot). Elk uur
maakt het systeem voor elk actief sjabloon de orders aan voor de datums binnen de horizon die nog geen
order van dit sjabloon hebben. Die orders zijn direct bevestigd en hebben een nummer.

## Orders aanmaken uit een sjabloon of kopiëren

**Nieuw van sjabloon** (in de lijst Opdrachten), **Orders aanmaken** (bij een sjabloon) en **Order
kopiëren** openen hetzelfde stappenvenster:

1. **Bron**: het sjabloon of de order die u kopieert.
2. **Datums**: kies *Per weekdag over een periode* (een begin- en einddatum en de weekdagen; standaard
   maandag tot en met vrijdag) of *Uit de kalender* (klik de gewenste dagen aan).
3. **Details**: het aantal exemplaren per datum (1 tot 20). De orders nemen de referentie, aantallen en
   gewichten van de bron over; pas die zo nodig daarna per order aan.
4. **Bevestigen**: een overzicht; met **Orders aanmaken** begint het aanmaken. Per datum ziet u of het
   gelukt is. Datums waarvoor dit sjabloon al een order heeft, worden overgeslagen.

## De lijst Ritten

Ritten maakt u niet in deze lijst aan: dat gebeurt op het Planbord, of via de "Tevens rit" van een order.
De lijst toont nummer, status, datum, naam, rittype, vervoerder, voertuigen, chauffeurs, gepland vertrek
en geplande aankomst, het aantal stops en legs, klanten, orders, afstand, beladingsgraad, CO₂, kosten,
opbrengst en waarschuwingen (een driehoekje; de muis erboven toont welke). Gearchiveerde ritten staan er
niet in. Filters: de weergave in het navigatiepaneel, bij *Alles* een periode, en verder status,
vervoerder, organisatie-eenheid en *Verberg lege ritten*.

In de actiebalk staan voor de geselecteerde rit(ten) **Vrachtbrief afdrukken**, **Ritten archiveren**,
**Afronden** en **Rit annuleren**. **Ritten archiveren** opent een stappenvenster met de geselecteerde
afgeronde of geannuleerde ritten; andere ritten worden overgeslagen en genoemd. Per rit ziet u of het
archiveren gelukt is.

## De ritpagina

De knoppen hangen af van de status: een *geplande* rit kunt u **Starten**, **Afronden** en **Rit
annuleren**; een rit *onderweg* **Afronden** en **Rit annuleren**; een *afgeronde* of *geannuleerde* rit
**Archiveren** of, als hij gearchiveerd is, **Dearchiveren**. **Afronden** vult ontbrekende werkelijke
tijden met de geplande. Bij annuleren gaan de legs van de rit af.

- **Algemeen**: nummer, ritdatum, naam, rittype, uitvoering, organisatie-eenheid, de stopsamenstelling en
  de regel voor eerste en laatste stop; vertrek en aankomst met geplande en werkelijke tijden; afstanden
  (gepland, werkelijk), geplande duur en beladingsgraad; opmerkingen, chauffeursinstructies en kenmerken.
  Vervoerder, voertuigen en chauffeurs worden op het Planbord gekozen en staan hier ter informatie, met
  een knop naar het Planbord. Alleen bij een geplande rit die vanuit een order is aangemaakt, wijzigt u
  ze hier met het potlood.
- **Legs**: de legs op de rit met order, klant, herkomst en bestemming, datum, tijdvenster, referenties,
  aantal, gewicht, afstand en de toegerekende kosten en opbrengst. Dubbelklik opent de order.
  Rechtsklikken geeft: **Legs losmaken** (van de rit halen), **Apart uitvoeren** (eigen stops voor deze
  leg), **Retourleg** (een leg terug van bestemming naar herkomst) en **Herkomstactiviteit uitbesteden**
  (het ophalen door een andere rit via een hub laten doen).
- **Stops**: de stops in volgorde met activiteit, relatie, plaats, geplande, verwachte en werkelijke
  tijden, cumulatieve afstand, het aantal legs en waarschuwingen. Rechtsklikken geeft **Omhoog** en
  **Omlaag** (niet bij een stop met werkelijke tijden), **Werkelijke tijden vastleggen** (aankomst,
  vertrek en de kilometerstand) en **Stop verwijderen** (alleen een handmatige stop). Met **Stop
  toevoegen** voegt u een handmatige stop toe, zoals een rustplaats.
- **Kosten en opbrengsten**: de kostenregels per leg en voor de rit, met **Regel toevoegen** voor een
  handmatige kostenregel; de totalen (kosten, opbrengst, marge) en de **verdeling per traject**: hoe de
  kosten en opbrengst over de legs zijn verdeeld.
- **Documenten**: de vrachtbrieven van de rit.

## Documenten

De lijst **Documenten** toont alle afgedrukte vrachtbrieven: nummer, afdrukmoment, order, het aantal
regels, profiel en bestand. Zoek op ordernummer met het zoekveld of beperk de periode met *van* en *tot*.
Rechtsklikken geeft **Downloaden**; dubbelklik opent de order.
