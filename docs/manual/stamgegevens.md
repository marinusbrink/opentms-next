# Stamgegevens

De app **Stamgegevens** beheert de referentielijsten waar orders, ritten en facturen naar
verwijzen. Deze eerste versie bevat tien lijsten, verdeeld over vier groepen in het
navigatiepaneel:

| Groep | Lijsten |
|---|---|
| Relaties | [Relaties](relaties.md), Adrestypen, Relatiecategorieën |
| Transport | Producten, Transporteenheden, Temperaturen |
| Wagenpark | [Chauffeurs](chauffeurs.md), Voertuigtypen, Transportkenmerken |
| Regio's en lijsten | Landen, Valuta's, Geografische regio's |

De app, elke groep en elke lijst is een eigen functie, die per tenant aan staat via de editie
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
  standaard is of als standaardvaluta van een land dient, een land dat in een postcodebereik
  voorkomt en een voertuigtype dat in een beladingsfactor voorkomt.
- Producten, transporteenheden en temperaturen kunnen worden **gearchiveerd** in plaats van
  verwijderd. Gearchiveerde records verdwijnen uit de keuzelijsten; met *Gearchiveerde tonen*
  blijven ze in de lijst zichtbaar.

## Meertalige namen

Valuta's, landen en adrestypen hebben een naam per taal (Nederlands en Engels). De Engelse naam
is verplicht en is de terugvaloptie; de lijst en de keuzelijsten tonen de naam in de taal van de
gebruiker.

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

## Voertuigtypen en transportkenmerken

Een voertuigtype heeft een soort (trekker, oplegger, bakwagen, bestelwagen), maximale EPQ en
laadmeters, standaardafmetingen en brutogewicht, en een set transportkenmerken. Een
transportkenmerk heeft een unieke code.

## Geografische regio's

Een regio bestaat uit postcodebereiken per land. Een bereik is óf numeriek (*van* tot en met
*tot*) óf een voorvoegsel:

- Een numeriek bereik vergelijkt de voorloopcijfers van een postcode: `1234 AB` valt in
  1000–1999. Een postcode zonder voorloopcijfers (bijvoorbeeld de Britse `SW1A 1AA`) valt nooit
  in een numeriek bereik.
- Een voorvoegsel vergelijkt het begin van de postcode zonder spaties en hoofdletterongevoelig:
  `SW` matcht `SW1A 1AA`.

Met het landfilter boven de lijst ziet u alleen regio's met een bereik in dat land.
