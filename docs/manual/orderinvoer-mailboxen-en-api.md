# Orderinvoer: mailboxen, API en importprofielen

> Deze pagina beschrijft hoe orders automatisch binnenkomen: per e-mail (Gmail of Microsoft 365) of via de API.
> Schermafbeeldingen volgen.

Klanten sturen hun transportorders op allerlei manieren: een PDF in een e-mail, een Excel-lijst, een XML-bestand, of
rechtstreeks vanuit hun eigen systeem. Met **orderinvoer** worden die orders in OpenTMS ingelezen zonder dat iemand ze
overtypt. Waar het systeem niet zeker is, controleert een medewerker de order op één plek: **Transport → Orderinvoer**
(zie [Transport](transport.md#orderinvoer)).

Zo werkt het, in het kort:

1. Een e-mail komt binnen in een gekoppelde mailbox, of een klantsysteem stuurt een order via de API.
2. OpenTMS bewaart het bericht en elke bijlage ongewijzigd, zodat je altijd kunt terugzien wat er binnenkwam.
3. Elk document wordt gelezen. Kent OpenTMS de opmaak al, dan leest een **importprofiel** het document, zonder AI en
   zonder kosten. Is de opmaak nieuw en staat AI aan, dan leert een taalmodel de opmaak één keer en stelt het een
   importprofiel voor.
4. De gelezen waarden (klant, adressen, transporteenheden, data) worden opgezocht in de stamgegevens, ook als er een
   typefout in staat. Wat niet bestaat, wordt aangemaakt (zie [Onbekende klanten en adressen](#onbekende-klanten-en-adressen)).
   Daarna wordt de order aangemaakt. Alleen als twee bestaande records even goed passen, of als er gegevens ontbreken
   (geen datum, geen laadadres), komt de order in de controlelijst.
5. Van elke stap is te zien wat er gebeurde: de **herkomst** (zie [De herkomst bekijken](#de-herkomst-bekijken)).

## Aan- en uitzetten

Orderinvoer is een functie per tenant, standaard **uit**. De host zet hem aan per tenant of editie (zie
[Tenants en edities](beheer-tenants-en-edities.md)):

| Functie | Wat het doet |
|---|---|
| **Orderinvoer** (`Integrations.OrderIntake`) | Gekoppelde mailboxen worden gelezen, de API antwoordt en ontvangen documenten worden orders. Staat hij uit, dan tonen de schermen van deze pagina dat de functie niet bij je pakket hoort. |
| **AI-mapping** (`Integrations.OrderIntake.AiMapping`) | Documenten die geen importprofiel kan lezen, gaan naar het taalmodel (Anthropic) om hun opmaak te leren. |

**Let op, privacy:** met AI-mapping aan gaat de inhoud van een onbekend document, met de namen van je ordersoorten,
stopsoorten en transporteenheden, naar Anthropic. Relatie- en adresgegevens uit je stamgegevens gaan nooit mee.
Anthropic traint niet op deze gegevens. Het aanzetten is de beslissing van de tenant.

De schermen staan in de app **Integraties**, groep **Orderinvoer** (Ontvangen, Mailboxen, API-clients,
Importprofielen, Instellingen), en in de app **Transport** (Orderinvoer). Mailboxen, API-clients en importprofielen zijn
geen stamgegevens: ze horen bij de koppelingen en staan daarom niet in de app Stamgegevens. De app **Integraties** en elk
navigatie-item zijn ook een eigen functie, standaard aan; zolang de functie **Orderinvoer** uit staat, tonen de schermen
dat de functie niet bij je pakket hoort.

## Een mailbox koppelen

OpenTMS leest één label (Gmail) of één map (Microsoft 365) van een mailbox. Het leest alleen: er wordt niets
verplaatst, gelabeld of als gelezen gemarkeerd. Alleen mail die **na** het koppelen in dat label of die map binnenkomt,
wordt ingelezen; oudere mail niet.

1. Ga naar **Integraties → Orderinvoer → Mailboxen**.
2. Klik op **Gmail koppelen** of **Microsoft 365 koppelen**.
3. Geef de mailbox een naam, bijvoorbeeld *Orders klantenservice*, en klik op **Koppelen**.
4. Je gaat naar Google of Microsoft. Log in met het account van de mailbox en sta het lezen van e-mail toe.
5. Je komt terug in OpenTMS; het venster van de mailbox opent met de melding dat je een map moet kiezen.
6. Kies bij **Label of map** het label of de map waar de orders in binnenkomen (bijvoorbeeld *Inbox/Orders*).
7. Kies eventueel een **Standaard klant** (voor mail waarvan de klant niet te herkennen is) en een **Standaard
   ordersoort**.
8. Klik op **Opslaan**. De status wordt *Actief*: vanaf nu wordt nieuwe mail in die map elke minuut opgehaald.

Lukt het koppelen niet (je annuleerde het inloggen, of de provider is nog niet ingesteld), dan meldt de lijst waarom.
De mailbox blijft staan met de status *Wacht op aanmelding*; met **Opnieuw koppelen** probeer je het nog eens.

<!-- SCREENSHOT: Integraties → Mailboxen met het venster Gmail koppelen -->

### Orders automatisch bevestigen

Met **Orders automatisch bevestigen** uit (de standaard) wordt elke order uit de mailbox een **concept** dat iemand
controleert en bevestigt. Zet je het aan, dan wordt een order direct bevestigd als:

- een importprofiel het document las (ook een nieuw, net door de AI geleerd en gecontroleerd profiel), of de order via
  de API als JSON binnenkwam, en
- elke waarde in de stamgegevens is gevonden of aangemaakt (ook als hij aan een vergelijkbare naam herkend is).

Las het taalmodel het document zonder gecontroleerd profiel (bijvoorbeeld een gescande PDF), dan blijft de order een
concept. Lukt het bevestigen niet (een regel van de ordersoort), dan blijft de order ook een concept; de reden staat in
de herkomst.

### Onbekende klanten en adressen

OpenTMS zoekt elke naam eerst exact op (code, btw-nummer, KvK-nummer, e-mailadres, postcode met huisnummer, naam). Vindt
het niets, dan zoekt het een **vergelijkbare naam**, zodat een typefout ("Heinekn" voor "Heineken") geen probleem is:

- Een klant met een vergelijkbare naam telt alleen als zijn btw-nummer, KvK-nummer en e-maildomein niet afwijken van
  wat de order noemt.
- Een adres telt alleen als de relatie een vergelijkbare naam heeft **en** het adres dezelfde postcode of dezelfde plaats
  heeft.
- Transporteenheden, producten en temperaturen worden op hun naam vergeleken.

Hoe sterk een naam moet lijken, stel je in bij **Integraties → Orderinvoer → Instellingen** (*Naamovereenkomst*, van
0,50 tot 0,95; standaard 0,75; zie [Instellingen](#instellingen)). Hoger is strenger: minder kans op een verkeerde klant, meer nieuwe
records. Lijken twee bestaande records ongeveer even goed (minder dan 0,1 verschil), dan kiest OpenTMS niet zelf en komt
de order in de controlelijst.

Lijkt niets genoeg, dan **maakt OpenTMS het record aan**:

- een **klant**: een nieuwe relatie met de rol *Klant*, met de naam, het e-mailadres, het btw- en KvK-nummer uit de
  order (geen naam: het domein van het e-mailadres van de afzender);
- een **laad- of losadres**: als er al een relatie met die naam is zonder (standaard)adres, krijgt die het adres;
  anders een nieuwe relatie met de naam van het adres (zonder naam: straat en plaats) en dat adres. Straat, postcode en
  plaats moeten in de order staan; zonder landcode wordt het standaardland van de tenant gebruikt, anders Nederland;
- een **transporteenheid, product of temperatuur**: een nieuw record met alleen de naam uit de order.

Ordersoort en stopsoort worden nooit aangemaakt; daarvoor gelden de standaardwaarden van de mailbox of API-client.

Alles wat zo is aangemaakt, herken je aan de omschrijving *Aangemaakt door de orderinvoer…*; een aangemaakte relatie
heeft daarnaast het externe kenmerk met bron `OrderIntake`. Controleer en vul deze records aan in de app Stamgegevens.
De orderinvoer onthoudt wat hij herkende of aanmaakte, zodat de volgende order met dezelfde tekst het meteen vindt.

### Statussen en acties

| Status | Betekenis | Wat je doet |
|---|---|---|
| **Wacht op aanmelding** | Het inloggen bij de provider is niet afgerond. | **Opnieuw koppelen**. |
| **Kies een map** | Gekoppeld, maar er is nog geen map gekozen. | Open de mailbox en kies de map. |
| **Actief** | De map wordt elke minuut gelezen. | Met **Pauzeren** stop je tijdelijk. |
| **Gepauzeerd** | Er wordt niets opgehaald. | Met **Hervatten** gaat het verder, vanaf waar het was. |
| **Opnieuw koppelen** | De provider accepteert de aanmelding niet meer (wachtwoord gewijzigd, toegang ingetrokken). Er gaat niets verloren. | **Opnieuw koppelen** met het account van de mailbox zelf. |

De kolommen **Laatst gecontroleerd** en **Laatste fout** laten zien of het ophalen loopt. **Verwijderen** stopt het
lezen; de toegang die bij Google of Microsoft is gegeven, wordt daarmee niet ingetrokken. Dat doe je in het
Google- of Microsoft-account zelf.

## API-clients

Een klant die orders vanuit zijn eigen systeem wil sturen, krijgt een **API-client**: een client-ID en een secret
waarmee zijn systeem een token opvraagt (OAuth client credentials).

1. Ga naar **Integraties → Orderinvoer → API-clients** en klik op **Nieuwe API-client**.
2. Vul een **Naam** in en kies de **Klant**: alle orders van deze client zijn orders van die klant. Kies eventueel een
   **Standaard ordersoort** en of orders **automatisch bevestigd** worden.
3. Klik op **Opslaan**. Er opent een venster met de **Client-ID**, het **Clientgeheim**, de **Token-URL**, de
   **Scope** (`OpenTms`) en een voorbeeld van de aanroep. Kopieer ze met de kopieerknoppen en geef ze veilig door
   aan de klant.

**Het secret wordt maar één keer getoond.** Kwijt? Kies **Nieuw secret maken**: het oude secret werkt dan direct niet
meer. Met **Deactiveren** weiger je tijdelijk alle aanroepen van de client; **Activeren** zet hem weer aan.
**Verwijderen** haalt de client definitief weg.

Het klantsysteem stuurt een order als JSON naar `/api/integrations/intake/orders` of een bestand naar
`/api/integrations/intake/documents`, steeds met een eigen uniek kenmerk (`externalId`). Hetzelfde kenmerk twee keer
sturen maakt geen tweede order. Een JSON-order wordt direct verwerkt en het antwoord zegt of de order is aangemaakt.

## Importprofielen en leren

Een **importprofiel** beschrijft hoe een bepaalde opmaak gelezen wordt: welke kolom, cel, regel of welk XML-element
welke waarde bevat. Profielen worden niet getypt maar **geleerd**:

1. Komt er een document binnen met een opmaak die nog geen profiel kent, en staat AI-mapping aan, dan leest het
   taalmodel het document en stelt het regels voor.
2. OpenTMS controleert de regels meteen: ze moeten precies dezelfde waarden lezen als het taalmodel. Alleen dan wordt
   het profiel opgeslagen, met de status **Voorgesteld**.
3. Elke order die met het profiel gelezen is en zonder correctie wordt bevestigd, telt mee. Na drie bevestigingen
   zonder correctie (in te stellen per tenant) wordt het profiel **Actief**. Ook een voorgesteld profiel mag al tot
   automatisch bevestigde orders leiden.
4. Verbetert iemand bij de controle een waarde, dan telt dat als correctie. Een actief profiel gaat terug naar
   *Voorgesteld* en wordt hersteld op basis van de verbeterde waarden (met AI, alleen bij een correctie).

Een bekende opmaak kost dus geen AI meer. Een gescande PDF zonder tekstlaag kan geen profiel krijgen; die wordt elke
keer door het taalmodel gelezen.

In **Integraties → Orderinvoer → Importprofielen** zie je per profiel de afzender, het formaat, de
status, de versie en hoe vaak het bevestigd en gecorrigeerd is. Open een profiel om:

- de **naam** te wijzigen (en op te slaan);
- op het tabblad **Regels** te zien welk veld waaruit gelezen wordt (*veld ← bron → bewerkingen*) en welke
  **kenmerken** een document moet hebben om het profiel te gebruiken;
- via **Herkomst bekijken** het document te openen waarvan het profiel geleerd is;
- het profiel met **Activeren** direct te vertrouwen, of met **Buiten gebruik stellen** niet meer te gebruiken.

De regels zelf zijn alleen-lezen: een verkeerde waarde verbeter je bij de controle van de order, het profiel leert
daarvan.

## Instellingen

Onder **Integraties → Orderinvoer → Instellingen** staat één instelling van je tenant:

- **Naamovereenkomst**: hoe sterk een ontvangen naam moet lijken op een bestaande klant, een bestaand adres, een
  transporteenheid, product of temperatuur om die te gebruiken in plaats van een nieuwe aan te maken. Kies een waarde van
  0,50 tot 0,95, in stappen van 0,05; standaard is 0,75. Hoger is strenger: er worden minder tikfouten geaccepteerd, dus
  er worden meer nieuwe gegevens aangemaakt. Lager is toleranter voor tikfouten, maar twee verschillende namen worden dan
  eerder voor dezelfde aangezien.

Kies de waarde en klik op **Opslaan**. De nieuwe waarde geldt voor orders die daarna binnenkomen; een order die al in de
controlelijst staat, houdt de waarde waarmee hij binnenkwam. Je ziet dit scherm met het recht *Instellingen orderinvoer*
(`Integrations.OrderIntakeSettings`); de vooraf ingestelde rol *Stamgegevensbeheerder* heeft dat recht.

<!-- SCREENSHOT: Integraties → Orderinvoer → Instellingen -->

## AI-instelling (host)

De host stelt de AI-leverancier en de koppelingen met Google en Microsoft in bij **Beheer → Diensten → Integraties**
(zie [Diensten](beheer-diensten.md#integraties)): de Anthropic-sleutel, het leermodel en het extractiemodel, en de
OAuth-gegevens van Google en Microsoft. Zonder Google- of Microsoft-gegevens kan een tenant die mailboxen niet
koppelen; zonder Anthropic-sleutel worden onbekende documenten niet geleerd en wachten ze in **Ontvangen**.

## De herkomst bekijken

In de app **Integraties → Orderinvoer → Ontvangen** staat elk bericht dat binnenkwam: ontvangen, kanaal, mailbox of API-client, van,
onderwerp, het aantal documenten en de status. Filter op status, kanaal en periode. Dubbelklik op een bericht om de
**herkomst** te openen:

- een tabblad per document (de tekst van de mail en elke bijlage), met bovenaan het kader **Details**. Dat is
  ingeklapt tot één regel: kanaal · mailbox of API-client · ontvangstmoment · status van het document · grootte ·
  **importprofiel** (klik om het profiel te openen). Klik op het pijltje om het uit te klappen; dan zie je alles:
  - het bericht: kanaal, mailbox of API-client, afzender, onderwerp, ontvangstmoment en bericht-ID;
  - het document: soort, status, formaat, grootte, hoe het gelezen is, het importprofiel, de opmaak-vingerafdruk, de
    afzendersleutel en **Origineel downloaden** (het bestand precies zoals het binnenkwam).

  Uitgeklapt blijft het kader uitgeklapt als je naar een ander document-tabblad gaat; open je de herkomst opnieuw, dan
  is het weer ingeklapt;
- daaronder, naast elkaar (op een smal scherm onder elkaar):
  - de **stappen**: wanneer wat gebeurde, met een pictogram voor gelukt, waarschuwing of fout. Bij een AI-stap staan
    het taalmodel, het aantal tokens en de kosten; bij een stap van een medewerker zijn naam. Herkende de orderinvoer
    iets aan een vergelijkbare naam of maakte hij iets aan, dan zegt de stap *Doorgegeven aan orderinvoer* hoeveel
    (bijvoorbeeld "1 herkend aan een vergelijkbare naam, 2 aangemaakt in de stamgegevens");
  - de **gelezen orders**: per order een **veldentabel** met elk veld, de waarde en waar die vandaan kwam (cel, regel
    met het stukje tekst, XML-element), met links naar de **orderinvoer** en de **order**.

De pagina zelf scrolt niet: zijn er veel stappen of velden, dan scrolt alleen de lijst met stappen of de lijst met
gelezen orders. Is het scherm te laag om beide lijsten een bruikbare hoogte te geven (op een smal scherm onder elkaar,
of met de details uitgeklapt), dan scrolt het tabblad zelf; de lijsten houden dan elk hun minimale hoogte.

In de actiebalk, groep **Orderinvoer**:

- **Beoordelen** opent de controlepagina in de app Transport van de order uit dit bericht die nog gecontroleerd moet
  worden. Wachten er meer, dan opent de eerste; na de controle brengt **Beoordelen** je naar de volgende. Wacht er geen
  enkele, dan is de knop uitgeschakeld en zegt de tooltip waarom. Je ziet de knop alleen met het recht *Orderinvoer*
  (`Orders.OrderIntakes`) en als het navigatie-item **Transport → Orderinvoer** aan staat.
- **Order openen** opent de transportorder als het bericht precies één order opleverde (met het recht
  *Transportorders* en het navigatie-item **Transport → Orders**). Leverde het meer orders op, dan open je ze via de
  links bij de gelezen orders.

Met **Bericht downloaden** haal je het hele oorspronkelijke bericht op (de e-mail als `.eml`). **Opnieuw verwerken**
leest het geopende document opnieuw, bijvoorbeeld nadat AI-mapping is aangezet of een profiel buiten gebruik is
gesteld; orders die al bestaan worden niet nog eens aangemaakt.

Vanuit de orderinvoer (**Herkomst bekijken**) en vanaf de orderpagina (de knop **Herkomst bekijken** naast *Import* of
*API*) kom je direct op de herkomst van het document waar de order uit kwam.

Een Customer Success Manager ziet deze herkomst door als gebruiker van de tenant mee te kijken (impersonatie).

<!-- SCREENSHOT: Integraties → Orderinvoer → Ontvangen, de herkomst van een bericht -->

## Rechten

| Recht | Technische naam | Wat het toestaat |
|---|---|---|
| **Mailboxen** | `Integrations.Mailboxes` | Mailboxen bekijken. |
| **Mailboxen koppelen en beheren** | `Integrations.Mailboxes.Manage` | Koppelen, opnieuw koppelen, wijzigen, pauzeren en verwijderen. |
| **API-clients** | `Integrations.ApiClients` | API-clients bekijken. |
| **API-clients aanmaken en beheren** | `Integrations.ApiClients.Manage` | Aanmaken, wijzigen, nieuw secret maken, (de)activeren en verwijderen. |
| **Importprofielen** | `Integrations.ImportProfiles` | Importprofielen bekijken. |
| **Importprofielen beheren** | `Integrations.ImportProfiles.Manage` | Activeren, buiten gebruik stellen, hernoemen en verwijderen. |
| **Ontvangen berichten** | `Integrations.InboundMessages` | Ontvangen berichten en hun herkomst bekijken, originelen downloaden. |
| **Opnieuw verwerken** | `Integrations.InboundMessages.Reprocess` | Een document opnieuw verwerken. |
| **Instellingen orderinvoer** | `Integrations.OrderIntakeSettings` | De naamovereenkomst voor herkenning bekijken en wijzigen. |
| **Orders via de API insturen** | `Integrations.IntakeApi.Submit` | Alleen voor API-clients, nooit voor een rol. |

De rechten voor de controlelijst staan bij [Transport](transport.md#orderinvoer).

De functies van de navigatie-items: `Apps.Integrations.Intake` (de groep Orderinvoer),
`Apps.Integrations.Intake.Received`, `Apps.Integrations.Intake.Mailboxes`, `Apps.Integrations.Intake.ApiClients`,
`Apps.Integrations.Intake.ImportProfiles` en `Apps.Integrations.Intake.Settings`, onder de app `Apps.Integrations`
(alle standaard aan). Een oude link naar
*Stamgegevens → Integraties* brengt je naar dezelfde lijst in de app Integraties.
