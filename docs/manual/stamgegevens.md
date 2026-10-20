# Stamgegevens

De app **Stamgegevens** beheert de referentielijsten waar orders, ritten en facturen naar
verwijzen, en dat voor **alle** domeinen: de stamgegevens van elk domein staan hier, nooit in de
app waarin dat domein zijn dagelijkse werk doet. Het navigatiepaneel heeft daarom drie niveaus:
eerst een groep per domein, daaronder de groepen met de lijsten. Nu zijn er twee domeingroepen:
**Algemeen** (de stamgegevens van het domein Stamgegevens zelf) en **Platform** (sjablonen,
documentsoorten en nummerreeksen, zie [Platform](#platform)); de groepen van andere domeinen, zoals
Orders of Financieel, verschijnen zodra hun eerste lijst er is.

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
laadmeters, standaardafmetingen en brutogewicht, en een set transportkenmerken. Een
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

## Platform

De domeingroep **Platform** bevat vier lijsten die het systeem zelf gebruikt bij het versturen van
e-mail, het maken van documenten, het archiveren van bestanden en het nummeren van orders, ritten,
facturen en inkooporders. Elke lijst is een eigen functie en staat standaard uit.

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
  vet, cursief, koppen, lijsten, koppelingen. Met de knop voor de HTML-weergave ziet en wijzigt u
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
- **Beschikbaar in klantportaal** laat klanten het document in het portaal downloaden.
- Een **gearchiveerde** definitie verschijnt niet meer in keuzelijsten en wordt niet automatisch
  gekozen.

Met het doelfilter boven de lijst ziet u alleen de definities voor dat doel.

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

### Nummerreeksen

Een nummerreeks bepaalt hoe nummers van orders, ritten, facturen en inkooporders worden opgebouwd.
Ordersoorten, ritsoorten, factuursoorten en inkoopordersoorten kiezen straks elk een nummerreeks;
zolang die soorten er nog niet zijn, legt u hier alleen de reeksen vast.

- **Schema** kiest de opbouw: viercijferig of tweecijferig jaar, jaar en maand, of alleen een
  volgnummer. Bij een jaar begint de teller elk jaar opnieuw bij 1, bij jaar en maand elke maand.
- **Voorvoegsel**, **voor/na jaar** en **voor/na maand** zijn vaste teksten in het nummer.
  **Minimum aantal cijfers** vult het volgnummer aan met nullen.
- Het scherm toont meteen hoe het volgende nummer eruitziet, bijvoorbeeld `O-26-0001`.
- Zodra er een nummer uit de reeks is uitgegeven, liggen de opbouwvelden vast en kan de reeks niet
  meer worden verwijderd (**Verwijderen** is dan uitgeschakeld); naam en omschrijving kunt u nog wijzigen. Zo ontstaan nooit dubbele
  nummers.
- Elk nummer wordt maar één keer uitgegeven, ook als twee gebruikers tegelijk een order aanmaken.
