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
voor het nieuwe record opent bovenop de order en na het opslaan is het gekozen. Met het toetsenbord: typ de
naam en druk op Enter zolang de lijst *Geen resultaten* toont. Zie *Nieuw record vanuit een keuzelijst* bij
Stamgegevens. Alleen de leg van een kostenregel (die volgen uit de orders) heeft de regel niet: daar valt
niets nieuws te maken.

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
- De kolom *Ritwaarschuwing* toont een driehoekje bij een order waarvan de "Tevens rit" niet is aangemaakt
  (zie *Tevens rit*); open de order voor de reden.
- **Exporteren** (het downloadpictogram rechts boven de lijst) maakt een Excel-bestand van de lijst. Kies
  *Zichtbare kolommen* of *Alle kolommen*, en *De rijen van het huidige filter en de zoekterm* of *Alle
  rijen* (alle orders, zonder filter of zoekterm). De kolommen staan in de volgorde en met de namen van de
  lijst; CO₂ gaat mee, de ritwaarschuwing niet. Een export heeft een maximum aantal rijen
  (standaard 50.000); is het er meer, dan meldt het venster dat en filtert u de lijst eerst.
- Dubbelklik op een regel om de order te openen.
- In de actiebalk: **Nieuwe transportorder** (opent direct een nieuwe orderpagina; heeft u meerdere
  ordertypen, dan staat er per type een knop), **Nieuw van sjabloon**, en voor de geselecteerde order(s) **Order kopiëren**, **Vrachtbrief
  afdrukken**, **Goedkeuren**, **Order annuleren** en **Verwijderen**. Rechtsklikken op een regel geeft
  dezelfde acties.

## Een order aanmaken

**Nieuwe transportorder** opent direct de orderpagina van een nieuwe order, zonder tussenvenster. Het
standaard ordertype is al gekozen (met **Nieuw** per ordertype het type van die knop), de orderdatum is
vandaag en de cursor staat in het veld *Klant*. Kiest u de klant, dan worden diens financiële
standaarden (valuta, btw, betaaltermijn, prijslijsten, kortingen en toeslagen) de financiële voorwaarden
van de order. Leg daarna de vrachtregels vast (zie *Vrachtregels invoeren*) en kies **Opslaan**
(of Ctrl+S, op de Mac ⌘+S): de order staat dan als *Concept* klaar, met alles wat u hebt ingevuld, en de
pagina toont de opgeslagen order. Met **Bevestigen** (of Ctrl+Enter, op de Mac ⌘+Enter) slaat u de nieuwe
order op en bevestigt u hem in één keer.

Een concept mag onvolledig zijn. Pas bij **Bevestigen** controleert het systeem of alles ingevuld is wat
het ordertype verplicht stelt (de velden met een sterretje), of er minstens één vrachtregel is en of elke
vrachtregel minstens twee stops heeft. Ontbreekt er iets, dan staat bovenaan het tabblad wat er mist. Bij
het bevestigen krijgt de order haar nummer en maakt de planning de legs (trajecten) aan.

## De orderpagina

De orderpagina volgt de indeling van het oude scherm. Bovenaan staat de **kop** met het nummer (of
*Concept*), de status en de knoppen. Rechts naast de knoppen staan de **tabbladen**:

1. **Algemeen**: de ordergegevens en daaronder de tabbladen **Vrachtregels**, **Extra diensten**,
   **Opmerkingen voor planner**, **Overige referenties** en, als uw organisatie kenmerken voor orders heeft
   gedefinieerd, **Eigen velden**;
2. **Financieel**: de prijsberekening, het omzetoverzicht, de facturatie, de opmerkingen voor de facturatie
   en de regels op orderniveau;
3. **Tevens rit**: een rit die bij het bevestigen direct wordt aangemaakt;
4. bij een sjabloon **Planning** (het schema);
5. drie tabbladen met alleen een symbool (houd de muis erboven voor de naam): **Documenten** (de paperclip;
   de afgedrukte vrachtbrieven, zodra de order is opgeslagen), **Opmerkingen** (de tekstballon; de
   opmerkingen met datum die bij de order zijn bewaard, zoals de reden van een herziening) en
   **Identificatie** (de vingerafdruk).

Een order heeft geen stoppatroon: stops horen bij een vrachtregel. De standaardstops van het ordertype
bepalen alleen met welke stops de invoer van een nieuwe vrachtregel begint.

De knoppen hangen af van de status:

| Status | Knoppen |
|---|---|
| Nieuw (nog niet opgeslagen) | Opslaan, Bevestigen |
| Concept | Opslaan, Bevestigen, Order annuleren, Vrachtbrief afdrukken, Verwijderen |
| Open | Opslaan, Goedkeuren, Order annuleren, Vrachtbrief afdrukken, Verwijderen |
| Gereed voor facturatie | Archiveren, Herzien, Vrachtbrief afdrukken |
| Gefactureerd | Archiveren, Vrachtbrief afdrukken |
| Gearchiveerd | Herzien, Vrachtbrief afdrukken |
| Geannuleerd | Vrachtbrief afdrukken |

Een order in de status *Gereed voor facturatie*, *Gefactureerd*, *Gearchiveerd* of *Geannuleerd* is
alleen te bekijken; de invoer van nieuwe vrachtregels staat er dan niet. Met **Opslaan als sjabloon**
maakt u van de order een sjabloon (alleen met het recht om sjablonen aan te maken). Drukt u een vrachtbrief
af terwijl er nog wijzigingen openstaan, dan slaat het systeem eerst op.

**Sneltoetsen** op de orderpagina:

| Toets | Doet |
|---|---|
| Ctrl+S (Mac: ⌘+S) | Opslaan (ook vanuit het venster *Vrachtregel*) |
| Ctrl+Enter (Mac: ⌘+Enter) | Bevestigen (alleen een concept of een nieuwe order) |
| Enter in de invoer van een nieuwe vrachtregel | De vrachtregel toevoegen |
| Enter of dubbelklik op een vrachtregel in de lijst | Het venster *Vrachtregel* openen |

**Opslaan** verstuurt de gewijzigde onderdelen van de order één voor één: de ordergegevens, de
vrachtregels, de referenties, de financiële voorwaarden, de rit en de kenmerken. Weigert het systeem een
onderdeel, dan stopt het opslaan daar. Bovenaan het geopende tabblad ziet u de melding en welke onderdelen
wel en welke niet zijn opgeslagen. De onderdelen die niet zijn opgeslagen, blijven gewijzigd staan; pas ze
zo nodig aan en kies opnieuw **Opslaan**: dan gaan alleen die onderdelen. Klopt er iets niet, dan opent het
systeem bij het opslaan het tabblad met de eerste fout (ook het tabblad binnen *Algemeen*); een tabblad
met een fout staat in rood met een uitroepteken. Een vrachtregel waar iets aan ontbreekt, heeft een rood
driehoekje in de lijst, en boven de lijst staat wat er mist, met het nummer van de vrachtregel en zo nodig
de stop (bijvoorbeeld *Vrachtregel 2 · Stop 2 (Lossen): kies een adres van de relatie*).

**Gewijzigd door iemand anders.** Heeft iemand anders de order gewijzigd nadat u begon met bewerken, dan
slaat het systeem uw wijzigingen niet over die van de ander heen. U krijgt de melding *Transportorder
gewijzigd door iemand anders* met twee keuzes:

- **Opnieuw laden** toont de versie van de ander; uw niet-opgeslagen wijzigingen vervallen.
- **Verder bewerken** houdt uw wijzigingen in beeld, bijvoorbeeld om ze over te nemen. Opslaan kan dan niet
  (de knop en Ctrl+S doen niets) tot u de order opnieuw laadt; bovenaan het tabblad staat een melding met
  de knop **Opnieuw laden**.

Onderdelen die vóór de melding al waren opgeslagen, blijven opgeslagen; de melding noemt welke wel en welke
niet. Alleen de berekende waarden (prijzen, CO₂, voortgang) veranderen tussendoor geregeld; dat is geen
wijziging door iemand anders en het opslaan gaat dan gewoon door.

- **Goedkeuren** controleert de order tegen de goedkeuringsregels van uw organisatie en toont per regel
  het resultaat (waarschuwing of fout) en de status die de order krijgt: *Gereed voor facturatie*, of
  *Gearchiveerd* als er niets te factureren valt. Alleen orders zonder fout worden goedgekeurd. Wijzigt er
  iets tussen controleren en goedkeuren, dan vraagt het systeem u opnieuw te controleren.
- **Herzien** zet een order uit *Gereed voor facturatie* of *Gearchiveerd* terug naar *Open*. U moet een
  reden opgeven; die wordt bij de order bewaard en staat daarna onder het tabblad **Opmerkingen**. Een
  gefactureerde order kan niet herzien worden.
- **Order annuleren** kan zolang geen leg op een rit staat die onderweg of afgerond is.

### Algemeen: de ordergegevens

Bovenaan het tabblad: het nummer (alleen te lezen; leeg tot de order bij het bevestigen een nummer
krijgt), de orderdatum, de organisatie-eenheid, het ordertype (vast zodra de order een nummer heeft), de
klant, de contactpersoon en de klantreferentie.

Kiest u bij een opgeslagen order een **andere klant**, dan vraagt het systeem *De financiële
standaardinstellingen van … overnemen?* Met **Overnemen** worden de valuta, btw, betaaltermijn,
prijslijsten en kortingen en toeslagen van de nieuwe klant de financiële voorwaarden van de order (zoals
bij een nieuwe order: de prijslijsten in hun volgorde en alleen de kortingen en toeslagen die op de
orderdatum gelden; een klant zonder financiële instellingen geeft euro zonder prijslijsten). Met
**Annuleren** blijven de huidige voorwaarden staan. In beide gevallen gelden ze zodra u de order opslaat:
de nieuwe klant en de voorwaarden worden in één keer opgeslagen, dus nooit de klant zonder zijn
voorwaarden. Onder het tabblad *Financieel* ziet u ze vooraf. Bij een nieuwe order vraagt het systeem
niets: de standaarden van de gekozen klant worden direct overgenomen.

### Algemeen: Vrachtregels

Van boven naar beneden: de invoer van een nieuwe vrachtregel, en daaronder de lijst met de vrachtregels
van de order.

#### Vrachtregels invoeren

De invoer bestaat uit twee delen.

1. De **stops** van de nieuwe vrachtregel, één rij per stop: activiteit, relatie, adres, contactpersoon,
   datum (bij een sjabloon de dagverschuiving), tijdvak, tijdvenster (*Van* en *Tot*, als UU:MM),
   referentie en opmerking. De rijen beginnen met de standaardstops van het ordertype (hun aantal en hun
   activiteiten; heeft het ordertype er geen, dan *Laden* en *Lossen*) op de orderdatum. Kiest u een
   relatie, dan neemt het systeem het standaardadres (of het enige adres) van die relatie. Met **+** voegt
   u na die rij een stop toe, met **−** haalt u een stop weg; een vrachtregel heeft altijd minstens twee
   stops.
2. Daaronder in één rij de **vrachtgegevens**: transporteenheid, aantal, product, posities, colli,
   gewicht, laadmeters en volume (velden die het ordertype verplicht stelt, hebben een sterretje), met de
   knoppen **Toevoegen** en **Plakken uit Excel**.

Zo werkt de invoer met het toetsenbord:

- **Tab** gaat naar het volgende veld; de agenda-knop van een datumveld slaat Tab over. Staat in een
  keuzelijst een keuze gemarkeerd, dan neemt Tab die over.
- **Enter** voegt de vrachtregel toe (behalve als er een keuzelijst openstaat: dan kiest Enter in die
  lijst). De nieuwe vrachtregel krijgt een kopie van de stops. De stops blijven daarna staan voor de
  volgende vrachtregel, de vrachtgegevens worden leeg behalve de transporteenheid, en de cursor staat in
  de relatie van de laatste stop. Zo voert u een reeks leveringen vanaf één adres in met alleen de laatste
  relatie, Enter, de laatste relatie, Enter.
- Ontbreekt er iets, dan voegt Enter niets toe en staat onder het veld wat er mist. Een melding over een
  stop noemt de stop, bijvoorbeeld *Stop 2 (Lossen): kies een adres van de relatie (of voeg er een toe aan
  de relatie)*. De cursor gaat naar dat veld.
- Kent het systeem een relatie, adres, activiteit, tijdvak, transporteenheid of product niet, typ dan de
  naam en druk op **Enter** terwijl de lijst *Geen resultaten* toont: het venster voor een nieuw record
  opent met die naam, en na het opslaan is het gekozen.

Lading, container en de overige gegevens vult u daarna in het venster *Vrachtregel* in.

#### Plakken uit Excel

Rijen uit Excel (of een ander rekenblad) worden in één keer vrachtregels. Kopieer de rijen en plak ze in
een veld van de invoer, of kies **Plakken uit Excel** en plak ze in het tekstveld. De kolommen staan in
deze volgorde: *Van*, *Naar*, *Datum*, *Tijd*, *Transporteenheid*, *Aantal*, *Gewicht*, *Referentie*. Een
datum mag ook de tijd bevatten (*26-10-2026 08:00*); die geldt als de kolom *Tijd* leeg is. Is de datum
leeg, dan geldt de orderdatum. Relaties en transporteenheden zoekt het systeem op code of naam. Een rij
wordt een vrachtregel met twee stops: de eerste en de laatste standaardstop van het ordertype (meestal
*Laden* en *Lossen*).

Eerst ziet u een **voorbeeld**. Een cel die het systeem niet kan thuisbrengen (geen of meer dan één
treffer, een ongeldige datum, tijd of hoeveelheid) staat in rood met een driehoekje; die velden blijven
leeg in de nieuwe vrachtregel en vult u daarna in het venster *Vrachtregel* in. Met de prullenbak laat u
een rij weg (bijvoorbeeld de kopregel). **… vrachtregels toevoegen** voegt de rijen toe; opslaan doet u
daarna met **Opslaan**.

#### De lijst met vrachtregels

De lijst toont per vrachtregel: het volgnummer, *Van → Naar* (de eerste en de laatste stop: relatie en
plaats), datum en tijd (van de eerste stop), transporteenheid, aantal, posities, gewicht, laadmeters,
referentie (van de eerste stop), de prijs (het opgeslagen totaal van de tariefregels van die vrachtregel;
tijdens het herberekenen staat er een draaiend symbool bij) en de rit (een knop naar de rit zodra de legs
van de vrachtregel op een rit staan). Onder de lijst staat het totaal van de vrachtregels. Transporteenheid,
aantal, posities, gewicht, laadmeters en referentie past u direct in de lijst aan.

Een vrachtregel verwijdert u met de prullenbak aan het eind van de regel; het systeem vraagt eerst
*Vrachtregel … verwijderen?* Dubbelklik op een vrachtregel, druk op Enter of klik op het potlood om het
venster *Vrachtregel* te openen.

#### Het venster Vrachtregel

Het venster heeft drie tabbladen:

- **Vrachtdetails**: eerst de lading en de aantallen (transporteenheid, aantal, posities, colli, gewicht,
  volume, laadmeters, europallets, beladingsgraad, product, klantcode, voertuigtype, temperatuur,
  transportvereisten, frankering, barcode), daaronder de tabbladen *Container*, *Metingen* (lengte, breedte,
  hoogte en drie **wegingen** en drie **volumes**; een leeg veld telt niet mee), *Teksten en documenten* en,
  als uw organisatie kenmerken voor vrachtregels heeft, *Eigen velden*.
- **Stops en trajecten**: de stops van de vrachtregel in dezelfde rijen als bij de invoer, met **+**, **−**
  en de pijltjes om een stop te verschuiven. Daaronder staan de **legs** van deze vrachtregel, met de
  geplande datums, afstand, CO₂, de rit en de toegerekende kosten en opbrengst. Dubbelklik op een leg op
  een rit om die rit te openen; bij een leg zonder rit (of via rechtsklikken **Bewerken**) wijzigt u per
  kant de referentie, de geplande datum en het **tijdvenster** (*Van* en *Tot*, als UU:MM op die datum;
  laat u beide leeg, dan geldt de hele dag), en de opmerking. Vóór het bevestigen staat hier dat de legs
  bij het bevestigen worden aangemaakt.
- **Prijzen**: de tariefregels van deze vrachtregel met het soort regel als label (houdt u de muis erboven,
  dan ziet u waar de prijs vandaan komt), aantal, eenheidsprijs en bedrag, en het totaal van de
  vrachtregel. Met het recht *Tariefregels wijzigen* voegt u een handmatige regel toe (**Regel
  toevoegen**), past u de eenheidsprijs van een berekende regel aan en verwijdert u een handmatige regel;
  dat gebeurt direct, los van **Opslaan**. Past u iets aan zonder op te slaan, dan staat hier na een
  ogenblik een **voorbeeld** van de nieuwe prijzen; een voorbeeld komt er pas als elke vrachtregel een
  transporteenheid heeft en minstens twee stops met relatie en adres. Een nieuwe vrachtregel krijgt zijn
  prijs na het opslaan.

Wat u in het venster wijzigt, is direct een wijziging van de order. **Toepassen** sluit het venster en
houdt de wijzigingen; **Sluiten** (of het kruisje of Esc) vraagt bij wijzigingen eerst *Niet-opgeslagen
wijzigingen* en zet met **Niet opslaan** de vrachtregel terug zoals hij was toen u het venster opende.
Opgeslagen wordt pas met **Opslaan** op de orderpagina (of Ctrl+S, ook in het venster).

### Algemeen: Extra diensten

De extra diensten van de order (bijvoorbeeld een laadklep of wachttijd), met **Extra dienst toevoegen**:
kies de dienst uit de lijst met extra diensten; grootboekrekening, kostenplaats en eenheidsprijs worden
daarbij ingevuld. Met de prullenbak verwijdert u een dienst. Dit gebeurt direct, los van **Opslaan**, en
kan pas als de order is opgeslagen; bij een nieuwe order staat hier dat u extra diensten toevoegt zodra de
order is opgeslagen.

### Algemeen: Opmerkingen voor planner, Overige referenties en Eigen velden

- **Opmerkingen voor planner**: vrije tekst voor de planning.
- **Overige referenties**: de referenties van de order (type, referentie en omschrijving), bijvoorbeeld een
  inkoopordernummer van de klant. **Referentie toevoegen** voegt een regel toe; type en referentie zijn
  verplicht (een type heeft hoogstens 32 tekens, een referentie 128, een omschrijving 256; hoogstens 50
  referenties per order).
- **Eigen velden**: de kenmerken die uw organisatie voor orders heeft gedefinieerd (in de app
  Stamgegevens, onder Orders). Het tabblad staat er alleen als er zulke kenmerken zijn.

### Financieel

Het tabblad heeft de blokken van het oude scherm:

- **Prijsberekening**: kies of de vrachtregels individueel worden geprijsd of dat de order een **vast
  bedrag** heeft (vul dan het bedrag in; kiest u weer voor individueel prijzen, dan vervalt het bedrag), de
  prijslijsten en wat er gebeurt als meerdere tarieven passen.
- **Omzetoverzicht**, alleen te lezen: de vrachtregels, een vaste prijs, de regels op orderniveau, de extra
  diensten, de kortingen en toeslagen en het totaal, met de valuta één keer erbij en een draaiend symbool
  zolang de prijzen worden herberekend. Tijdens een voorbeeld staan hier de voorbeeldbedragen. Bij een
  nieuwe order staat hier dat de omzet wordt berekend zodra de order is opgeslagen.
- **Facturatie**: valuta (een keuzelijst van de valuta's uit Stamgegevens; een ontbrekende voegt u toe met
  *Nieuwe valuta toevoegen…*), btw, kortingen en toeslagen (die van de klant zijn gemarkeerd als *standaard
  klant*), betaaltermijn en of de vrachtdocumenten met de factuur meegaan.
- **Opmerkingen m.b.t. facturatie**: vrije tekst voor de facturatie.
- **Regels op orderniveau** (zodra de order is opgeslagen): handmatige regels op de order, de vaste prijs en
  de kortingen en toeslagen, met **Regel toevoegen** en **Vaste prijs instellen** (of weer verwijderen).

De financiële standaarden van de klant worden overgenomen als u de klant kiest (zie *Algemeen: de
ordergegevens*).

### Tevens rit

Zet het vinkje *Maak bij het bevestigen direct een rit aan voor deze order* aan. Het systeem vult de lege
velden dan alvast in: het standaard rittype (of het enige rittype), het vertrek met de relatie en het adres
van de eerste stop van de eerste vrachtregel op de rit, de aankomst met die van de laatste stop van de
laatste vrachtregel op de rit, en als geplande vertrekdatum de datum van die eerste stop (met het begin van
het tijdvenster als tijd, als dat is ingevuld). Controleer de voorstellen en pas ze zo nodig aan; wat u al
had ingevuld, blijft staan.

Onder **Vrachtregels op de rit** staat per vrachtregel een vinkje; standaard gaan ze allemaal mee. Alleen
de legs van de aangevinkte vrachtregels gaan bij het bevestigen op de rit; de andere blijven ongepland
voor het Planbord. De stops van de rit volgen uit die legs. Staat er geen enkele vrachtregel aangevinkt
(bijvoorbeeld omdat de enige gekozen vrachtregel is verwijderd), dan meldt het tabblad dat; vink dan
vrachtregels aan of zet de tevens-rit uit, anders kunt u niet opslaan.

Onder **Geavanceerd** kiest u desgewenst de vervoerder, de voertuigen of een combinatie, de chauffeurs en
een afgesproken inkoopprijs met valuta. Met een vervoerder wordt de rit uitbesteed aan die vervoerder; de
afgesproken inkoopprijs hoort daarbij en is pas in te vullen als er een vervoerder is gekozen (haalt u de
vervoerder weg, dan vervalt ook de prijs). Zonder vervoerder rijdt de rit met eigen materieel en komen de
kosten uit de kostprijs per km. Bij **Bevestigen** maakt het systeem de rit één keer aan. Daarna is dit
tabblad alleen te bekijken en staat er een knop naar de aangemaakte rit; latere wijzigingen aan de order
veranderen de rit niet.

Kon de planning de rit niet aanmaken (bijvoorbeeld omdat het rittype, een relatie, voertuig of chauffeur
daar niet bekend is), dan staan de legs ongepland. Dit tabblad toont dan een waarschuwing met de reden en
het tijdstip, en bovenaan de orderpagina staat het label *Rit niet aangemaakt*; een klik erop opent dit
tabblad. Plan de legs dan op het Planbord.

### Documenten

De afgedrukte vrachtbrieven van deze order, met nummer, afdrukmoment, profiel en bestand. Met het
download-symbool haalt u de PDF op; met **Vrachtbrief afdrukken** maakt u een nieuwe.

### Opmerkingen

De opmerkingen die bij de order zijn bewaard, met datum en tijd en de nieuwste bovenaan, bijvoorbeeld de
reden van een herziening. Ze zijn alleen te lezen.

## Vrachtbrief (CMR) afdrukken

**Vrachtbrief afdrukken** opent een stappenvenster:

1. **Profiel**: het standaardprofiel is al gekozen. U kunt een ander profiel kiezen en voor deze afdruk
   het aantal exemplaren, randen, QR-code en *groeperen per leg* aanpassen, en per vak (afzender,
   geadresseerde, plaats van aflevering, plaats van inontvangstneming, vervoerder, opvolgende vervoerder,
   opgemaakt te) bepalen waar de gegevens vandaan komen.
2. **Vrachtregels**: vink de vrachtregels aan die op de vrachtbrief komen. Bij een rit gaan alle orders van de rit mee.
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

Een sjabloon heeft dezelfde pagina als een order, met deze verschillen: in plaats van het nummer en de
orderdatum heeft het een **naam** en *Actief*, de stops hebben een **dagverschuiving** (aantal dagen na de
orderdatum) in plaats van een datum (ook bij de invoer van een nieuwe vrachtregel), er is geen
omzetoverzicht, de tabbladen *Documenten* en *Opmerkingen* ontbreken, en er is een tabblad **Planning**
(het schema):
herhaling (geen, wekelijks of maandelijks), interval, de weekdagen of de dag van de maand, de horizon
(hoeveel dagen vooruit het systeem orders aanmaakt, 1 tot 60) en de geldigheid (van en tot). Elk uur
maakt het systeem voor elk actief sjabloon de orders aan voor de datums binnen de horizon die nog geen
order van dit sjabloon hebben. Die orders zijn direct bevestigd en hebben een nummer.

## Orders aanmaken uit een sjabloon of kopiëren

**Nieuw van sjabloon** (in de lijst Opdrachten), **Orders aanmaken** (bij een sjabloon) en **Order
kopiëren** openen hetzelfde stappenvenster:

1. **Bron**: het sjabloon of de order die u kopieert. Maakt u hier met *Nieuw sjabloon toevoegen…* een
   sjabloon aan, dan sluit het stappenvenster en opent de pagina van het nieuwe sjabloon: een nieuw
   sjabloon heeft nog geen regels. Voeg die daar toe en kies dan **Orders aanmaken**. Een sjabloon zonder
   regels kan nog geen orders aanmaken (de knop is dan uitgeschakeld, in de lijst en op de pagina).
2. **Datums**: kies *Per weekdag over een periode* (een begin- en einddatum en de weekdagen; standaard
   maandag tot en met vrijdag) of *Uit de kalender* (klik de gewenste dagen aan).
3. **Details**: het aantal exemplaren per datum (1 tot 20), en per aan te maken order (datum en exemplaar)
   de klantreferentie en per regel het aantal en het gewicht. Links staat de lijst van de orders die worden
   aangemaakt, rechts de gekozen order; alles begint met de waarden van de bron (het sjabloon of de
   gekopieerde order). Een potloodje in de lijst markeert een order waarvan u iets hebt gewijzigd. Alleen
   wat u wijzigt, gaat mee; een leeggemaakt aantal of gewicht houdt de waarde van de bron, een leeggemaakte
   klantreferentie maakt die leeg. Regels toevoegen of verwijderen kan hier niet.
4. **Bevestigen**: een overzicht, met het aantal orders met gewijzigde details; met **Orders aanmaken**
   begint het aanmaken. Per datum ziet u of het
   gelukt is. Datums waarvoor dit sjabloon al een order heeft, worden overgeslagen.

## De lijst Ritten

Ritten maakt u niet in deze lijst aan: dat gebeurt op het Planbord, of via de "Tevens rit" van een order.
De lijst toont nummer, status, datum, naam, rittype, vervoerder, voertuigen, chauffeurs, gepland vertrek
en geplande aankomst, het aantal stops en legs, klanten, orders, afstand, beladingsgraad, CO₂, kosten,
opbrengst en waarschuwingen (een driehoekje; de muis erboven toont welke). Gearchiveerde ritten staan er
niet in. Filters: de weergave in het navigatiepaneel, bij *Alles* een periode, en verder status,
vervoerder, organisatie-eenheid en *Verberg lege ritten*. **Exporteren** (het downloadpictogram rechts
boven de lijst) werkt als bij de lijst Opdrachten: zichtbare of alle kolommen, de rijen van het filter of
alle rijen (gearchiveerde ritten blijven buiten de export). Tijden staan in uw tijdzone, de afstand in km en
de beladingsgraad als percentage; CO₂ gaat mee in kg.

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
  een knop naar het Planbord. Bij een rit met eigen materieel staat als vervoerder *Eigen bedrijf*; bij een
  uitbestede rit de vervoerder (of *Nog geen vervoerder gekozen*) en, als die is afgesproken, de
  afgesproken inkoopprijs. Alleen bij een geplande rit die vanuit een order is aangemaakt, wijzigt u
  ze hier met het potlood.
- **Legs**: de legs op de rit met order, klant, herkomst en bestemming, datum, tijdvenster, referenties,
  aantal, gewicht, afstand en de toegerekende kosten en opbrengst. Dubbelklik opent de order.
  Rechtsklikken geeft: **Bewerken** (per kant de referentie, geplande datum en het tijdvenster, en de
  opmerking; zoals op het tabblad Planning van de order), **Legs losmaken** (van de rit halen), **Apart
  uitvoeren** (eigen stops voor deze leg), **Retourleg** (een leg terug van bestemming naar herkomst) en
  **Herkomstactiviteit uitbesteden** (het ophalen door een andere rit via een hub laten doen). Bij
  uitbesteden kiest u de hub (relatie en adres) en de **doelrit** uit een zoeklijst van geplande en lopende
  ritten (zoeken op nummer, naam, vervoerder of plaats); de rit van de leg zelf staat er niet in.
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
