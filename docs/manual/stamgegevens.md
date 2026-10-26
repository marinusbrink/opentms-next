# Stamgegevens

De app **Stamgegevens** beheert de referentielijsten waar orders, ritten en facturen naar
verwijzen, en dat voor **alle** domeinen: de stamgegevens van elk domein staan hier, nooit in de
app waarin dat domein zijn dagelijkse werk doet. Het navigatiepaneel heeft daarom drie niveaus:
eerst een groep per domein, daaronder de groepen met de lijsten. De domeingroepen zijn **Algemeen** (de
stamgegevens van het domein Stamgegevens zelf), **Orders**, **Planning & Uitvoering**, **Financieel** en
**Platform** (e-mailsjablonen, rapportdefinities en documentsoorten, zie [Platform](#platform)).

Onder **Algemeen** zijn de lijsten verdeeld over vier groepen:

| Groep | Lijsten |
|---|---|
| Relaties | [Relaties](relaties.md), Adrestypen, Relatiecategorieën |
| Transport | Producten, Transporteenheden, [Productrelatiecodes](#productrelatiecodes), Temperaturen |
| Wagenpark | [Voertuigen en Voertuigcombinaties](voertuigen.md), [Chauffeurs](chauffeurs.md), Voertuigtypen, Transportkenmerken |
| Regio's en lijsten | Landen, Valuta's, Geografische regio's |

De app, elke domeingroep, elke groep en elke lijst is een eigen functie, die per tenant aan staat via de editie
(zie [Tenants en edities](beheer-tenants-en-edities.md)). Een lijst waarvan de functie uit staat,
ziet u niet in het navigatiepaneel; opent u hem toch via een link, dan meldt het scherm dat het
niet bij uw pakket hoort. Keuzelijsten in een scherm (bijvoorbeeld het land in een relatie) werken
zolang de app aan staat, ook als de lijst zelf uit staat. Elke lijst heeft eigen rechten
voor bekijken, aanmaken, wijzigen en verwijderen; zonder het recht om te wijzigen opent een
record alleen-lezen.

## Werken met een lijst

- Een lijst is een tabel met zoeken, sorteren en kolommen kiezen (zie [Actiebalk](actiebalk.md)).
  Dubbelklikken op een regel opent het record; rechtsklikken toont **Bewerken** en **Verwijderen**.
- **Nieuw** staat vooraan in de actiebalk. **Selectie verwijderen** verwijdert de aangevinkte
  regels; regels die nog in gebruik zijn worden overgeslagen en de melding noemt ze.
- Een record dat in gebruik is kan niet worden verwijderd. Dat geldt voor een valuta die de
  standaard is of als standaardvaluta van een land dient, een land dat in een postcodebereik of als registratieland van een voertuig
  voorkomt, een voertuigtype dat in een beladingsfactor of bij een voertuig voorkomt, en een
  product, transporteenheid of relatie waar een productrelatiecode naar verwijst.
- Producten, transporteenheden en temperaturen kunnen worden **gearchiveerd** in plaats van
  verwijderd. Gearchiveerde records verdwijnen uit de keuzelijsten; met *Gearchiveerde tonen*
  blijven ze in de lijst zichtbaar.

## Nieuw record vanuit een keuzelijst

Elke keuzelijst in een scherm die een record kiest (een land, valuta, voertuigtype, product,
relatie, chauffeur, organisatie-eenheid en zo verder) eindigt met de regel **Nieuw … toevoegen…**,
bijvoorbeeld *Nieuw land toevoegen…* of *Nieuwe relatiecategorie toevoegen…*. Zo hoeft u het scherm
niet te verlaten (en uw wijzigingen niet kwijt te raken) als het record dat u zoekt nog niet bestaat:

- De regel opent het scherm voor een nieuw record bovenop het scherm waarin u werkt. Wat u in de
  keuzelijst had getypt, staat al in het veld *Naam* (bij een chauffeur: één woord is de achternaam,
  bij meer woorden is het eerste woord de voornaam).
- Na **Opslaan** sluit dat scherm en is het nieuwe record gekozen; in een keuzelijst met meerdere
  waarden (bijvoorbeeld de categorieën van een relatie) wordt het aan de keuze toegevoegd. **Sluiten**
  zonder opslaan laat de keuzelijst zoals hij was.
- Dat geldt ook voor de keuzelijsten in een tabel in het scherm, zoals het land van een
  postcodebereik of het voertuigtype van een beladingsfactor: het nieuwe record komt in die regel.
- Een adres en een contactpersoon horen bij een relatie. *Nieuw adres toevoegen…* en *Nieuwe
  contactpersoon toevoegen…* openen daarom het scherm van de gekozen relatie (een contactpersoon met het
  venster voor de nieuwe contactpersoon al open en de getypte naam erin). Na **Opslaan** van de relatie
  is het nieuwe adres of de nieuwe contactpersoon gekozen. Zonder gekozen relatie staat de regel er niet;
  u hebt het recht nodig om relaties te wijzigen.
- Een nieuwe relatie vanuit een keuzelijst die alleen klanten (of alleen vervoerders) toont, krijgt die
  rol al mee, zodat de keuzelijst haar daarna terugvindt.
- Dit werkt in elke app, ook in **Transport**: het scherm dat opent is hetzelfde als in Stamgegevens.
- De regel staat er alleen als u het recht hebt om zo'n record aan te maken én het onderdeel van
  dat record voor uw organisatie aanstaat (bijvoorbeeld *Stamgegevens › Referentie › Landen* voor
  een land), en niet in een scherm dat alleen-lezen is geopend.
- In een keuzelijst in een tabel kiest **Tab** de gemarkeerde waarde en gaat naar de volgende cel.
  Met **pijl omlaag** voorbij de laatste waarde komt u op de regel **Nieuw … toevoegen…**; **Enter**
  opent dan het scherm voor het nieuwe record. Keuzelijsten met vaste waarden (een soort, een blokkeerreden,
  een brandstof) en de filters boven een lijst hebben de regel niet.

## Meertalige namen

Valuta's, landen, adrestypen en documentsoorten hebben een naam per taal (Nederlands en Engels).
De Engelse naam is verplicht en is de terugvaloptie; de lijst en de keuzelijsten tonen de naam in de
taal van de gebruiker. In het scherm vult u de naam in uw eigen taal in; de namen in de andere talen
blijven bewaard. Heeft het record nog geen Engelse naam, dan wordt uw naam ook de Engelse naam.

## Valuta's en landen

Bij het aanmaken van de tenant worden alle ISO 4217-valuta's en alle ISO 3166-landen
ingelezen. Alleen de euro staat op **Actief**; andere valuta's activeert u zelf. Keuzelijsten
tonen alleen actieve valuta's. Precies één valuta is de **standaard** van de tenant; wijzig die
met **Als standaard instellen** in het record van een andere, actieve valuta.

Een land heeft een standaardvaluta en een tijdzone (IANA-naam, bijvoorbeeld
`Europe/Amsterdam`). De ISO-codes (alpha-2, alpha-3, numeriek) zijn uniek per tenant. Hoogstens één
land is het **standaardland** van de tenant (**Als standaard instellen** in het record van het land);
een nieuw adres van een relatie begint met dat land en zijn tijdzone, en het standaardland kan niet
worden verwijderd. Een
herstart van de inlezing voegt alleen ontbrekende rijen toe; namen en instellingen die u heeft
aangepast blijven staan, en een verwijderde rij komt niet terug.

## Adrestypen

Een adrestype heeft een **soort**: bezoekadres, postadres of overig. Per tenant bestaat
hoogstens één bezoekadres-type en één postadres-type, zodat documenten en stops altijd weten
welk adres van een relatie bedoeld is. Van de soort *overig* kunt u er zoveel aanmaken als u
wilt.

## Producten, transporteenheden en temperaturen

- Een **product** heeft standaardwaarden per aantal (aantal, gewicht, volume, laadmeters,
  EPQ, afmetingen) en een ADR-tabblad. Bij een ADR-product zijn het UN-nummer (vier cijfers) en de
  klasse verplicht; daarnaast legt u de classificatiecode, verpakkingsgroep, juiste vervoersnaam,
  technische benaming, gevaarsetiketten, het gevaarsidentificatienummer (Kemler), de
  tunnelbeperkingscode, de vervoerscategorie (0–4, voor de 1000-puntenregel), het vlampunt en de
  bijzondere bepalingen vast, met de vinkjes *gelimiteerde hoeveelheid*, *milieugevaarlijk* en
  *afval* zoals ze op het vervoersdocument horen.
- Een **transporteenheid** is een verpakkingstype: afmetingen, leeggewicht, maximale belading,
  laadmeters, EPQ-factor, of de eenheid stapelbaar is en hoe hoog, en of de eenheid wordt geruild
  (europallet). Op het tabblad *Beladingsfactoren* legt u per
  voertuigtype vast hoeveel eenheden erin passen; elk voertuigtype komt hoogstens één keer voor.
- Een **temperatuur** heeft een minimum en maximum in graden Celsius.
- Alle drie hebben het tabblad **Niet combineren met**: de records die niet samen in één rit
  mogen. De koppeling geldt in beide richtingen.

## Productrelatiecodes

Een **productrelatiecode** is de artikelcode die een klant voor zijn goederen gebruikt, gekoppeld
aan uw eigen gegevens: een **bestemming** (een relatie), een **product** en een
**transporteenheid**. Imports en orderregels die de code van de klant noemen, vinden zo het juiste
product, de verpakking en het afleveradres.

- De **code** wordt opgeslagen zoals u hem typt (zonder spaties aan begin en eind) en is uniek
  binnen de tenant, ongeacht hoofdletters: `abc-1` en `ABC-1` zijn dezelfde code.
- Minstens één van bestemming, product en transporteenheid is verplicht; een code die nergens naar
  verwijst heeft geen zin. De bestemming kiest u door in het veld te typen; met **Nieuwe relatie
  toevoegen…** maakt u de relatie meteen aan.
- De lijst toont code, bestemming (naam en plaats), product en transporteenheid. Zoeken werkt op
  code, naam van de bestemming en naam van het product; de lijst wordt per pagina van de server
  gehaald, ook bij tienduizenden codes.
- Een code die u niet meer gebruikt, **archiveert** u met het vinkje *Gearchiveerd* in de editor. Imports
  herkennen een gearchiveerde code niet meer; met *Toon gearchiveerd* blijft hij in de lijst
  zichtbaar.
- Een import die een onbekende code noemt, maakt die code **niet** zelf aan: de code moet eerst
  hier bestaan.

## Voertuigtypen en transportkenmerken

Een voertuigtype heeft een soort (trekker, oplegger, bakwagen, bestelwagen), maximale EPQ en
laadmeters, standaardafmetingen en brutogewicht, een **kostprijs per km** (voor ritten met eigen
materieel; een voertuig zonder eigen kostprijs gebruikt die van zijn type, en de lijst toont de kolom)
en een set transportkenmerken. Een
transportkenmerk heeft een unieke code. Een nieuw [voertuig](voertuigen.md) neemt de afmetingen van
zijn type één keer over; de transportkenmerken van het type gelden altijd voor elk voertuig van dat
type.

## Geografische regio's

Een regio bestaat uit postcodebereiken per land. Een bereik is óf numeriek (*van* tot en met
*tot*) óf een voorvoegsel:

- Een numeriek bereik vergelijkt de voorloopcijfers van een postcode: `1234 AB` valt in
  1000–1999. Een postcode zonder voorloopcijfers (bijvoorbeeld de Britse `SW1A 1AA`) valt nooit
  in een numeriek bereik.
- Een voorvoegsel vergelijkt het begin van de postcode zonder spaties en hoofdletterongevoelig:
  `SW` matcht `SW1A 1AA`.

Met het landfilter boven de lijst ziet u alleen regio's met een bereik in dat land.

## Orders

Onder **Orders › Transportorders** staan, naast de ordersoorten, waypointsoorten en tijdvakken, drie lijsten
voor de vrachtbrief (CMR) en de eigen kenmerken van een order.

### Vrachtbriefprofielen

Een vrachtbriefprofiel bepaalt hoe een CMR wordt afgedrukt. Eén profiel is de **standaard**: dat gebruikt het
systeem als bij het afdrukken geen ander profiel wordt gekozen. Met **Als standaard instellen** (rechtsklikken in
de lijst, of bovenin het scherm van een opgeslagen profiel) maakt u een ander profiel de standaard; de vorige
vervalt. Het standaardprofiel kunt u niet verwijderen en een gearchiveerd profiel kan geen standaard zijn.

- **Algemeen**: de naam, het aantal **Exemplaren** (1 tot 5), **Kaders tonen**, de QR-code, **Groeperen per
  traject** en de **Bestandlayout**: de rapportdefinitie met het doel *Vrachtbrief (CMR)* die de opmaak levert.
  Laat u die leeg, dan geldt de standaarddefinitie voor de vrachtbrief in de taal van de gebruiker.
- **Vakken**: per partijvak (vak 1 afzender, 2 geadresseerde, 3 plaats van aflevering, 4 plaats van inontvangstneming,
  16 vervoerder, 17 opvolgende vervoerder, 21 opgemaakt te) kiest u waar de gegevens vandaan komen: de klant, het
  laad- of losadres, het eigen bedrijf, de vervoerder, de verlader, het vertrek- of aankomstpunt van de rit, of
  leeg. Met de vinkjes kiest u de goederenkolommen van vak 6–12, de instructies van vak 13 en de opmerkingen van
  vak 18. **Vaste teksten** voegt teksten uit de lijst hieronder toe aan de vakken 6 en 13.

### Vaste teksten

Een vaste tekst (bijvoorbeeld *Breekbaar, niet stapelen*) staat op de vrachtbrief als een profiel hem kiest. Een
tekst heeft een naam en de tekst zelf (maximaal 2000 tekens). Een tekst die een profiel gebruikt, kunt u niet
verwijderen; archiveer hem dan: hij verdwijnt uit de keuzelijsten, het profiel houdt hem.

### Orderkenmerken

Orderkenmerken zijn de eigen velden van uw organisatie op een order of op een orderregel (bijvoorbeeld een
zegelnummer). Een kenmerk heeft een **Geldt voor** (order of regel), een **Sleutel** (het vaste kenmerk dat
koppelingen gebruiken, maximaal 32 tekens, uniek per *Geldt voor*), een naam en een positie (de volgorde in het
orderscherm). Met het filter boven de lijst ziet u alleen de kenmerken van de order of van de regels. Een
gearchiveerd kenmerk kan niet meer worden ingevuld; de waarden die al op orders staan blijven leesbaar.

## Planning & uitvoering

Onder **Planning & uitvoering › Ritten** staan, naast de ritsoorten, de via-depotregels en de ritkenmerken.

### Via-depotregels

Een via-depotregel laat de trajecten van een order via een depot lopen in plaats van rechtstreeks: het eerste
traject eindigt op het depot, het volgende vertrekt daar.

- **Bereik** zegt voor welke orders de regel geldt: een **Relatie** (een laad- of losadres van die relatie), een
  **Klant** (alle orders van die klant) of een **Regio** (een adres in die geografische regio). Bij relatie en klant
  kiest u de relatie, bij regio de regio.
- **Depot** is de relatie van het depot, **Depotadres** een van de adressen van die relatie (kies eerst het depot).
  Een nieuw adres voegt u toe in het scherm van de relatie.
- **Activiteit bij aankomst op het depot** is verplicht; **Activiteit bij vertrek van het depot** is optioneel
  (leeg: dezelfde als bij aankomst). **Overnachting op het depot** betekent dat de goederen een nacht op het depot
  blijven.
- **Positie**: passen meerdere regels, dan geldt de eerste in positievolgorde.

### Ritkenmerken

Ritkenmerken zijn de eigen velden van uw organisatie op een rit. Een kenmerk heeft een **Sleutel** (een letter,
daarna letters, cijfers of liggende streepjes; maximaal 32 tekens), een naam en een positie.

## Financieel

Onder **Financieel › Prijzen** staan, naast de aanvullende diensten en de kortingen en toeslagen, de prijslijsten
en de financiële instellingen per relatie.

### Prijslijsten

Een prijslijst rekent een prijs uit voor een orderregel (omzet) of voor een traject van een vervoerder (kosten).
De lijst toont code, naam, soort, valuta, basis, geldigheid en het aantal regels; met het filter boven de lijst
ziet u alleen omzet- of kostenlijsten. Een lijst die een relatie of een berekende prijsregel gebruikt, kunt u niet
verwijderen; archiveer hem dan. Het scherm van een prijslijst vult het hele venster.

- **Algemeen**: code, naam, **Soort** (omzet of kosten), valuta, geldigheid (*Geldig vanaf* en *Geldig tot*; buiten
  die periode rekent een order de lijst niet) en de berekening: de **Basis** (waarover de prijs per eenheid gaat,
  bijvoorbeeld het gewicht of de kilometers), de **Vermenigvuldiging**, de **Kolomhoeveelheid** (welke hoeveelheid
  de kolom kiest), de **Stopvergelijking** (of de eerste, de laatste of beide stops moeten passen) en de
  **Omrekenfactor eenheid** (de hoeveelheid wordt door deze factor gedeeld, bijvoorbeeld 1000 voor kg naar ton).
- **Kolommen**: de horizontale staffels. Elke kolom geldt tot en met zijn grens; de grenzen lopen op en de laatste
  kolom is open (*Daarboven*). Maximaal 50 kolommen.
- **Regels**: links de regels van de lijst, rechts de gekozen regel. Een regel beschrijft een **laan** (aan de
  *Van*- en *Naar*-kant een relatie of een regio, minstens aan één kant) en optioneel een transporteenheid, product
  en voertuigtype (*Alle* als u niets kiest). Het **Niveau** is het aantal gekozen kenmerken (0 tot 3): de meest
  specifieke regel wint; met het filter toont u alleen **Uitzonderingen** (niveau 1–3), **Standaard** (niveau 0) of
  één niveau. Onder de regel staan de **Staffels**: per rij een grens op de **Rijhoeveelheid** en per kolom van de
  lijst een prijs per eenheid (leeg = geen prijs in die cel); de laatste rij is open. Met *Ingevoerd* als
  rijhoeveelheid heeft de regel alleen die open rij. **Minimumbedrag** en **Maximumbedrag** begrenzen het bedrag van
  de regel. Een regel slaat u op met **Regel opslaan**, los van de lijst; wijzigt u de kolommen, sla de lijst dan
  eerst op.
- **Simuleren** (met het recht daarvoor): beschrijf één orderregel (de relaties en regio's van van en naar, de
  transporteenheid, de hoeveelheden en een peildatum) en kies **Simuleren**. U ziet welke regel wint, welke kolom
  die kiest, de prijs per eenheid en het bedrag, en de andere kandidaten met de reden waarom ze niet wonnen. Er wordt
  niets opgeslagen.

Bovenin het scherm van een opgeslagen lijst staan vier knoppen; met niet-opgeslagen wijzigingen zijn ze uitgeschakeld:

- **Kopiëren** maakt een nieuwe lijst met dezelfde kolommen en regels, met een eigen code, naam en geldigheid, en
  opent die.
- **Aanpassen** wijzigt alle prijzen van de lijst met een percentage of een bedrag, afgerond naar keuze (op de cent,
  5 cent, 25 cent, 50 cent of hele eenheden). Met **Voorbeeld** ziet u eerst hoeveel cellen veranderen en een paar
  voorbeelden; daarna kiest u **Toepassen**. De aanpassing is niet ongedaan te maken.
- **Exporteren** downloadt de regels, staffels en prijzen als Excel-bestand.
- **Importeren** vervangt alle regels van de lijst door die uit een Excel-bestand in de vorm van de export (maximaal
  5 MB). Een fout in het bestand wordt gemeld met de rij en de kolom; dan verandert er niets.

### Financiële instellingen relatie

Per relatie legt u vast hoe orders van die relatie worden geprijsd en gefactureerd. De lijst toont per relatie het
aantal prijslijsten, de btw, de betaaltermijn, de vaste prijs en de valuta; zoeken werkt op de naam van de relatie.
De instellingen ontstaan bij de eerste keer opslaan (daarvoor is het recht om te wijzigen nodig) en worden niet
verwijderd. Kiest u bij **Nieuw** een relatie die al instellingen heeft, dan bewerkt u die.

- **Algemeen**: de relatie (vast zodra de instellingen bestaan), de **Btw** (leeg: de standaard btw), de
  **Betaaltermijn (dagen)**, de valuta (leeg: de valuta van de relatie), **Standaard vaste prijs** en
  **Vrachtdocumenten met de factuur meesturen**.
- **Prijslijsten**: de **Prijslijsten omzet** en de **Prijslijsten kosten**, elk in volgorde en elke lijst één keer
  per kant (maximaal 20). Per kant bepaalt **Meerdere passende lijsten** wat er gebeurt als meer dan één lijst een
  prijs geeft: **Alle toepassen**, **Hoogste wint** of **Laagste wint**.
- **Standaard kortingen en toeslagen**: elke nieuwe order van deze relatie begint met deze kortingen en toeslagen,
  binnen hun geldigheid (*Geldig vanaf* verplicht, *Geldig tot* optioneel).

## Platform

De domeingroep **Platform** bevat drie lijsten die het systeem zelf gebruikt bij het versturen van
e-mail, het maken van documenten en het archiveren van bestanden. Elke lijst is een eigen functie en staat
standaard uit. (De nummering van orders, ritten en facturen staat op de soort zelf: zie de ordersoorten,
ritsoorten en factuursoorten.)

### E-mailsjablonen

Een e-mailsjabloon bepaalt hoe een bericht eruitziet dat het systeem verstuurt, bijvoorbeeld een
orderbevestiging of een factuur. Het scherm is opgebouwd als een e-mail, zoals in het oude OpenTMS.

- **Doel** zegt waarvoor het sjabloon is (transportopdracht, rit, factuur, betalingsherinnering,
  inkooporder, ETA-melding, chauffeursinstructies, digitaal archief, proforma-overzicht). Per doel
  is één sjabloon de **standaard**; maakt u een ander sjabloon standaard, dan vervalt de vorige.
- **Taal**: een sjabloon geldt voor één taal, of voor **alle talen**. Maak voor elke taal een eigen
  sjabloon. Zodra het systeem deze mails verstuurt, kiest het het sjabloon van het doel in de taal
  van de ontvanger, en anders de standaard.
- **Van**, **Antwoordadres**, **Aan**, **Cc** en **Bcc** zijn de adressen van het bericht. Laat u
  **Aan** leeg, dan gaat het bericht naar de ontvanger die bij het record hoort (bijvoorbeeld de
  klant van de factuur). Met **Stuur mij een kopie** krijgt de verzender zelf een kopie.
- **Onderwerp** en **Inhoud** zijn verplicht. De inhoud bewerkt u zoals in een e-mailprogramma:
  vet, cursief, koppen, lijsten, koppelingen en afbeeldingen (via een webadres; het bedrijfslogo is
  `{{ company.logo_url }}`). Met de knop voor de HTML-weergave ziet en wijzigt u
  de HTML zelf, bijvoorbeeld voor een herhaling `{{ for … }}`.
- Een nieuw sjabloon begint niet leeg: **Van** en **Antwoordadres** staan op het e-mailadres van uw
  eigen bedrijf (`{{ company.email }}`, voor factuurdoelen `{{ company.invoice_email }}`), en
  **Onderwerp** en **Inhoud** krijgen de standaardtekst van het gekozen doel in de gekozen taal.
  Kiest u een ander doel of een andere taal, dan wisselt die tekst mee, zolang u er zelf nog niets in
  hebt getypt. Een bestaand sjabloon wordt nooit overschreven.
- Teksten tussen `{{` en `}}` zijn **tags**: velden die bij het versturen worden ingevuld, bijvoorbeeld
  `{{ company.name }}` of `{{ invoice.number }}`. Het paneel **Tags** naast het formulier toont alle
  tags die voor het gekozen doel beschikbaar zijn, per groep (eigen bedrijf, ontvanger, order, factuur,
  …) met een korte uitleg; met het zoekvak filtert u de lijst. Klik op een tag om hem in te voegen op
  de plek van de cursor in het veld waar u het laatst was (Van, Aan, Onderwerp, de inhoud, …). Een
  lijsttag (bijvoorbeeld de vrachtregels van een order) voegt een herhaalblok `{{ for … }}` in, dat
  per regel wordt herhaald. Een fout in deze schrijfwijze (bijvoorbeeld een ontbrekende `}}`) wordt bij
  opslaan gemeld, met het veld en de regel.

Met het doelfilter boven de lijst ziet u alleen de sjablonen voor dat doel.

### Rapportdefinities

Een rapportdefinitie verwijst naar de opmaak waarmee een document (orderbevestiging, ritlijst,
factuur, inkooporder, emballageoverzicht) als PDF wordt gemaakt.

- **Doel** en **Taal** bepalen waarvoor de definitie dient. Per doel en taal is één definitie de
  **standaard**.
- **Renderprovider** en **Sjabloon-id bij provider** wijzen de opmaak bij de documentdienst aan.
- **Bestandsnaampatroon** bepaalt de naam van het PDF-bestand, bijvoorbeeld `factuur-{{ number }}.pdf`.
  Het enige veld dat u kunt gebruiken is `{{ number }}`: het nummer van het document (bij een
  emballageoverzicht de naam van de periode). Een patroon met een ander veld, zoals
  `{{ invoice.number }}`, wordt bij opslaan geweigerd.
- **Beschikbaar in klantportaal** laat klanten het document in het portaal downloaden.
- Een **gearchiveerde** definitie verschijnt niet meer in keuzelijsten en wordt niet automatisch
  gekozen.

Met het doelfilter boven de lijst ziet u alleen de definities voor dat doel.

#### Sjabloon bewerken, kopiëren en testen

Bij een bestaande rapportdefinitie staan bovenin de groep **Sjabloon** drie knoppen:

- **Sjabloon bewerken** opent de opmaak in de editor van de documentdienst, in een nieuw tabblad van
  de browser. Onder **Sjabloon-id bij provider** ziet u de naam van het sjabloon bij de
  documentdienst, of de melding dat het sjabloon daar niet bestaat.
- **Sjabloon kopiëren** zet na uw bevestiging een kopie van het sjabloon in uw eigen werkruimte bij
  de documentdienst en maakt een nieuwe rapportdefinitie die die kopie gebruikt (naam met
  "(kopie)", niet standaard). Die nieuwe definitie gaat direct open.
- **Testdocument** maakt een PDF met voorbeeldgegevens en downloadt die. Een testdocument is gemarkeerd
  als voorbeeld (PREVIEW) en wordt niet in rekening gebracht.

Een knop wacht hooguit 20 seconden op de documentdienst. Antwoordt die niet op tijd of is hij bezet,
dan ziet u een melding: probeer het dan zo nog eens.

De standaardsjablonen van FiLogic worden door alle klanten gedeeld en kunt u niet zelf bewerken.
Wilt u er een aanpassen, kopieer het dan eerst: u bewerkt daarna de kopie in uw eigen werkruimte.

De knoppen werken alleen als de functie **Documenten maken** voor uw omgeving aanstaat (FiLogic zet
die aan via uw pakket) en FiLogic de koppeling met de documentdienst heeft ingericht. Kan een knop
niet worden gebruikt, dan is hij grijs; houd de muis erop om de reden te zien. Hebt u in het venster nog niet
opgeslagen wijzigingen, dan zijn de drie knoppen grijs tot u hebt opgeslagen: ze werken met het opgeslagen
sjabloon. Ziet u een knop
helemaal niet, dan hebt u er geen recht op (**Rapportsjablonen bewerken** voor bewerken en kopiëren,
plus **Rapportdefinities aanmaken** voor kopiëren).

### Documentsoorten

Een documentsoort deelt bestanden in het digitale archief in, bijvoorbeeld vrachtbrieven of
algemene documenten. Elke tenant begint met **FREIGHT** (Vrachtdocument) en **GENERAL** (Algemeen
document); deze mag u aanpassen.

- **Code** is uniek en wordt in hoofdletters opgeslagen. **Naam** vult u in uw eigen taal in; de
  namen in de andere talen blijven bewaard (zie [Meertalige namen](#meertalige-namen)).
- **Vrachtdocument**: het bestand is zichtbaar in de portalen en gaat mee in OTM-berichten.
- **Meesturen met factuur**: het bestand gaat als bijlage mee met de factuurmail.
- **Tonen in lijstindicator**: het bestand telt mee in het bijlagen-symbool in lijsten.
- Op het tabblad **Transformaties** legt u vast wat er met een binnenkomend bestand gebeurt, in
  volgorde: afbeelding omzetten naar PDF, koppelen op bestandsnaam, hernoemen naar een eigenschap van
  het gekoppelde record, of een PDF splitsen op een referentiepatroon. Elke bewerking komt hooguit
  één keer voor. Hernoemen en splitsen hebben een argument nodig; voor splitsen is dat een reguliere
  expressie.
- Een **gearchiveerde** documentsoort verschijnt niet meer in keuzelijsten; met *Toon gearchiveerd*
  blijft hij in de lijst zichtbaar en kunt u hem weer activeren.
