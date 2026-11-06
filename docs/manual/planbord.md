# Planbord

> **Vertaling:** deze pagina is de bronversie (Nederlands). De Engelse vertaling
> wordt gegenereerd als onderdeel van de documentatiestap na deze draft.

Het planbord gebruikt de hele breedte van het scherm. Panelen open je met **Paneel toevoegen** in
de werkbalk.

---

## Lijsten: filter van het planbord

De lijsten *Transportritten*, *Transportorders* en *Te plannen legs* volgen de periode en de
organisatie-eenheden van de werkbalk van het planbord. Zet je in een lijst **Filter van het
planbord gebruiken** uit, dan verschijnen in die lijst een eigen periode en eigen eenheden. De
periode, de eenheden en je eigen lijstinstellingen (kolommen, groepering) bewaart het planbord per
gebruiker; de volgende keer staan ze weer zo.

Wijzigingen van collega's verschijnen vanzelf in de lijsten. Kom je terug in het venster of was
de verbinding even weg, dan halen de lijsten opnieuw op wat je ziet.

### Een rit openen

Dubbelklik je ergens op het planbord op een rit (of kies je **Rit openen**), dan opent de rit in
de Transport-app. Heeft je organisatie de Transport-app niet, dan schuift rechts een paneel open
met de rit: de gegevens van de rit, de voertuigen en chauffeurs, de stops en de legs. Dat paneel
is alleen om te lezen; wijzigen doe je in de lijsten en de planner van het planbord. Sluit het
met het kruisje of Esc.

Sluit je een rit of order die je vanaf het planbord hebt geopend, dan kom je terug op het planbord.
De geselecteerde rit en stop staan er dan nog.

### Legs buiten het ritvenster

Sleep je legs naar een rit terwijl hun datums buiten het venster van die rit vallen, dan vraagt
het planbord eerst **Buiten het ritvenster plannen?** en noemt het het venster van de rit. Kies
**Toch plannen** om de legs toch aan de rit toe te voegen, of breek af: dan blijven de legs waar
ze waren. Dit geldt overal waar je legs op een rit zet: in *Transportritten*, de planner en de
kaart.

---

## Transportritten

De ritten waarvan de geplande uitvoering in de periode valt (gearchiveerde ritten niet).

- **Nieuwe rit** opent hetzelfde venster als in de Transport-app. Na het opslaan blijf je op het
  planbord en is de nieuwe rit geselecteerd.
- **Klik** op een rit om hem te selecteren: *Ritstops*, *Legs* en de kaart tonen dan die rit.
  Selecteer je meerdere ritten, dan blijven zij de laatst enkel geselecteerde rit tonen.
- **Dubbelklik** (of Enter) opent de rit (zie *Een rit openen*).
- **Naam**, **Opmerkingen** en **Chauffeursinstructies**: ga op de cel staan en klik op het
  potlood. Enter of verder klikken slaat op, Esc breekt af.
- **Vervoerder**, **Voertuigen** en **Chauffeurs**: het potlood in de cel opent een keuzelijst in
  de cel zelf (alleen bij geplande of lopende ritten). Kies je een andere vervoerder, dan vervalt de
  contactpersoon; die kies je via **Middelen** in het rechtermuismenu.
- Met de rechtermuisknop: **Rit openen**, **Middelen**
  (vervoerder, voertuigen, chauffeurs), **Starten**, **Afronden** (je vult het werkelijke vertrek
  en de werkelijke aankomst in; stops zonder werkelijke tijden krijgen die ingevuld), **Stops
  optimaliseren** (de nieuwe volgorde verschijnt zodra de berekening klaar is), **Archiveren**
  (afgeronde of geannuleerde ritten), **Rit annuleren** en **Verwijderen**. Annuleren en
  verwijderen vragen eerst om bevestiging.
- **Sleep legs** uit *Te plannen legs* op een rit om ze toe te wijzen. Komen ze uit *Legs* (van
  een andere rit), dan worden ze verplaatst. Vallen ze buiten het ritvenster, dan vraagt het
  planbord eerst of je ze toch wilt plannen.
- **Sleep een chauffeur** uit *Resources* op een rit om hem toe te voegen (maximaal drie).

Kolommen met berekende waarden (beladingsgraad, afstand, CO₂, kosten, opbrengst) tonen een
draaiend teken zolang de berekening loopt.

---

## Transportorders

De transportorders die in de periode gepland staan. Het gaat om de geplande
datums van de stops, niet om de orderdatum: een order staat in de lijst als de periode van zijn
eerste tot zijn laatste stop de periode van het planbord raakt. Kies je organisatie-eenheden,
dan toont de lijst de orders van al die eenheden. Dubbelklik opent de order in de Transport-app.

Selecteer je één order, dan kun je in *Te plannen legs* **Alleen order …** aanvinken om alleen de
ongeplande legs van die order te zien.

- **Klantreferentie** en **Interne opmerkingen**: ga op de cel staan en klik op het potlood.
- **Sleep een order** aan het greepje naar een rit (in *Transportritten*, op een balk in de planner
  of op de samenvatting op de kaart): alle legs van de order gaan naar die rit. Staan er legs op een
  andere rit, dan vraagt het planbord eerst of ze daar weg mogen en noemt het die ritten.
- Laat je de order op een lege plek in de planner los, dan komt er een nieuwe rit met die legs.
- Legs die niet weg kunnen blijven staan: legs op een afgeronde of geannuleerde rit en legs waarvan
  de stops al werkelijke tijden hebben. Het planbord noemt ze in een melding.

---

## Ritstops

De stops van de geselecteerde rit als tijdlijn, van boven naar beneden, met per stop de tijden, de
relatie, de activiteit, de plaats, de orders, het tijdvenster en de opmerkingen.

- **De tijden** staan links, aankomst boven vertrek. Een groen vinkje betekent werkelijke tijden,
  een vrachtwagen verwachte tijden; zonder teken zijn het de geplande tijden.
- **De lijn** is doorgetrokken voor het deel dat de rit heeft gereden en gestippeld voor wat nog komt.
- **De markering Nu** laat zien waar de rit nu is: bij een stop of onderweg, met de vertraging ten
  opzichte van de planning. Ze schuift elke minuut mee en verdwijnt als de rit is afgerond. Zijn
  alle verwachte tijden voorbij en is er niets gemeld, dan blijft ze bij de laatste stop staan.
- **Op tijd**, **Krap** of **… min te laat** vergelijkt de verwachte aankomst met het tijdvenster.
- **Klik** op een stop om hem te selecteren; hij wordt dan ook op de kaart geselecteerd, en andersom.
- **Dubbelklik** (of Enter) opent het venster met de stopgegevens.
- **Sleep** een stop aan het greepje naar een andere stop om hem op die plek te zetten. Ook kan
  het met de rechtermuisknop: **Omhoog** en **Omlaag**. Een stop met werkelijke tijden blijft
  staan.
- **Opmerkingen**: ga met de muis op de stop staan en klik op het potlood.
- **Werkelijke tijden vastleggen** (rechtermuisknop): aankomst, vertrek en de kilometerstand.
- **Stop toevoegen** voegt een handmatige stop toe; **Stop verwijderen** (rechtermuisknop) kan
  alleen bij een handmatige stop.

### Het venster Stop

- **Algemeen**: adres en contactgegevens, de geplande en verwachte tijden (om te lezen), en een kaart.
  Je wijzigt hier de **werkelijke aankomst en vertrek**, de **opmerkingen** en de **geplande duur**.
  Bij een stop zonder legs kun je ook de **relatie en het adres** kiezen.
- **Legs**: de legs van deze stop.

Geplande en verwachte tijden berekent het systeem; die kun je niet met de hand wijzigen.

Je hebt het recht *stops beheren* nodig om stops te wijzigen.

---

## Legs

De legs van de geselecteerde rit: order, klant, herkomst en bestemming, datums, tijdvensters,
aantallen, referenties, opmerkingen, afstand en de toegerekende kosten en opbrengst.

- **Referenties en opmerkingen** (dubbelklik of rechtermuisknop) wijzigt de referenties van
  herkomst en bestemming en de opmerkingen.
- **In de cel wijzigen** (ga op de cel staan en klik op het potlood): referenties, opmerkingen,
  tijdvensters en datums, en klantreferentie, transporteenheid, product en temperatuur. Die laatste
  vier horen bij de order en gelden daarom voor alle legs van die order of vrachtregel. Een leg op een
  afgeronde of geannuleerde rit wijzig je niet meer.
- **Legs losmaken** zet de geselecteerde legs terug in *Te plannen legs*.
- **Apart uitvoeren** geeft de leg eigen stops.
- **Sleep** legs aan het greepje naar een andere rit (in *Transportritten*, de planner of de
  kaart) om ze te verplaatsen. Heb je meerdere legs geselecteerd, dan gaan die allemaal mee.

---

## Te plannen legs

De legs zonder rit met een herkomstdatum in de periode.

- **Groeperen op**: **Datum herkomst**, **Klant**, **Plaats herkomst** of **Plaats bestemming**.
  Elke groep toont het aantal legs; klap een groep open voor de legs.
- Selecteer legs door op een rij te klikken (met Ctrl of Shift erbij selecteer je er meer) of met de
  vinkjes, en **sleep** ze aan het greepje naar een rit, een rij of balk in de planner of de kaart.
- De velden wijzig je in de cel, net als bij *Legs*.
- Met de rechtermuisknop: **Transportorder openen**, **Deeltraject toevoegen of aanpassen**,
  **Regel splitsen**, **Leg splitsen op …** en **Retourleg**.
- **Deeltraject toevoegen of aanpassen** opent het venster **Deeltrajecten beheren** met alle legs van
  de vrachtregel. Voeg een leg toe of verwijder er een; de legs sluiten vanzelf op elkaar aan: kies je
  een bestemming, dan wordt die de herkomst van de volgende leg. Het begin en het einde van de route
  liggen vast. Een leg die op een rit staat of werkelijke tijden heeft, kun je hier alleen bekijken.
- **Regel splitsen** splitst de hele vrachtregel. Je geeft het aantal op en ziet gewicht, colli,
  volume, laadmeters, laadfactor en EPQ per deel meerekenen. De order krijgt een tweede vrachtregel
  en elke leg een kopie. De kopie van de leg die je koos komt in *Te plannen legs*; de kopieën van
  de andere legs komen op dezelfde rit als het origineel, tenzij dat origineel niet meer kan wijzigen
  (afgeronde of geannuleerde rit, of werkelijke tijden): dan blijft de kopie ongepland.
- **Leg splitsen op …** maakt van de leg twee legs die elkaar treffen op een adres dat je kiest,
  bijvoorbeeld een depot of overslagpunt. Beide legs blijven in *Te plannen legs* staan.
- Organisatie-eenheden filteren deze lijst niet: legs hebben (nog) geen eenheid.

---

## Planner (Scheduler)

De planner toont je resources als rijen tegen een tijdas, met elke rit als balk. Je kunt de
planner meerdere keren openen (**Paneel toevoegen** > **Planner**), bijvoorbeeld een planner
voor trekkers en een voor chauffeurs. Elke planner onthoudt zijn eigen instellingen.

### Instellingen bovenin

- **Modus**: **Trekker** (trekkers, bakwagens en bestelwagens), **Oplegger** of **Chauffeur**.
  Elke rij is dan een voertuig of een chauffeur.
- **Periode**: kies de eerste dag en de lengte: **Dag**, **3 dagen**, **Week** of **2 weken**
  (maximaal 14 dagen). Met de pijltjes ga je een periode terug of vooruit.
- **Zoom**: **Uren** of **Dagen** per kolom.
- **Rijen zoeken**: zoek op naam, kenteken of code.
- **Filter van het planbord gebruiken**: staat dit aan, dan volgt de planner de periode en de
  organisatie-eenheden van de werkbalk van het planbord. Kies je in de planner zelf een
  periode, dan gaat dit vinkje uit en houdt de planner zijn eigen periode en eenheden aan.
- **Velden op de balk**: kies wat een balk toont: nummer, naam, status, vervoerder,
  chauffeurs, oplegger, eerste en laatste plaats, beladingsgraad en waarschuwingen.

Onder de instellingen staan de kengetallen: het aantal rijen, ritten, ritten onderweg, rijen
zonder rit en de bezetting.

### Balken en blokken

- De kleur van een balk volgt de status van de rit (gepland, onderweg, afgerond).
- Een driehoekje op de balk betekent dat de rit waarschuwingen heeft. Ga erop staan voor de lijst.
- Grijze en rode vlakken in een rij zijn blokken: rood is **niet beschikbaar** (verlof, ziek,
  onderhoud, keuring …), grijs is **buiten werktijd**.
- Een balk met een gestippelde rode rand zit in een **conflict**: hij overlapt een andere rit
  in dezelfde rij of een blok.

### Werken met de planner

- **Klik** op een balk om de rit te selecteren; de stops, legs en de kaart volgen die rit.
  Met Ctrl (of Cmd) klik je meerdere ritten aan.
- **Dubbelklik** op een balk om de rit te openen in de Transport-app.
- **Sleep** een balk in de tijd om de rit te verplaatsen. Sleep hem naar een andere rij om de
  trekker, oplegger of chauffeur te wisselen. Beide tegelijk kan ook.
- Komt de rit daarmee in een conflict of op een blok, dan vraagt het planbord eerst of je hem
  **toch wilt verplaatsen**. Het planbord houdt je niet tegen; de rit krijgt een waarschuwing.
- Afgeronde en geannuleerde ritten kun je niet verslepen.
- **Sleep over een lege plek** in een rij om daar een nieuwe rit te maken. Je kiest het
  rittype; het standaard rittype staat al klaar.
- **Sleep legs** uit *Te plannen legs* op een lege plek in een rij: er komt een
  nieuwe rit met die legs, op die tijd en dat voertuig of die chauffeur. Zijn er meer rittypes, dan
  kies je er eerst een. Laat je ze op een balk los, dan gaan ze naar die rit. Vallen ze buiten
  het ritvenster, dan vraagt het planbord eerst of je ze toch wilt plannen. Lukt het slepen niet,
  bijvoorbeeld omdat je niet op een rij loslaat of geen recht hebt, dan meldt het planbord waarom.
- Laat je legs op een blok (bijvoorbeeld verlof) los, dan vraagt het planbord eerst of je er toch
  een rit wilt maken.
- **Sleep een chauffeur** uit *Resources* op een balk om hem aan de rit toe te voegen (maximaal
  drie chauffeurs per rit).
- **Sleep een chauffeur** op een lege plek in een rij: het venster voor een nieuwe rit opent op
  die plek, met die chauffeur al ingevuld. Na het opslaan staat de chauffeur op de nieuwe rit.
  Mag je geen ritten maken, dan kun je een chauffeur alleen op een balk loslaten.
- **Sleep een balk naar de kaart** aan het greepje vooraan op de balk: de kaart toont dan die
  rit.

Wijzigingen van collega's verschijnen vanzelf in de planner.

---

## Kaart

De kaart toont de geselecteerde rit en, als je dat wilt, de andere ritten van de periode.

- **De geselecteerde rit**: de route over de weg, met trajecten met tol in een eigen kleur.
  Is er nog geen route berekend, dan staat er een gestippelde rechte lijn tussen de stops.
  De stops zijn genummerd en gekleurd: **uitgevoerd**, **op tijd**, **krap** (de verwachte
  aankomst ligt kort voor het einde van het tijdvenster) en **te laat**. Klik op een stop voor
  het tijdvenster, de geplande en verwachte aankomst en de werkelijke tijden. De stop wordt dan
  ook in *Ritstops* geselecteerd, en andersom.
- **Samenvatting**: linksboven staan de afstand, de duur, het tolbedrag, het aandeel
  tolkilometers en de CO₂-uitstoot. Met **Tol per traject** klap je de toltrajecten uit. Is er
  nog geen route berekend, dan staat dat in de samenvatting. In een lage kaart scrol je door de
  samenvatting.
- **Alle ritten**: elke rit van de periode als lijn van de eerste naar de laatste stop. Klik op
  een lijn om die rit te selecteren.
- **Een rit uit de planner**: sleep een balk aan zijn greepje naar de kaart om die rit te
  tonen.
- **Ongeplande legs in de buurt**: zet dit aan en kies een straal. De ongeplande
  legs bij de stops van de rit verschijnen als ruitjes. Klik op een ruitje en kies
  **Aan rit toevoegen**, of sleep het ruitje naar de samenvatting van de rit.
- Met **Passend maken** zoom je naar de rit (of naar alle ritten).
- **Legenda** linksonder klapt de uitleg van de kleuren open en weer dicht.

Milieuzones en afgesloten gebieden komen in een volgende versie.

---

## Resources

Hier zie je je chauffeurs en voertuigen met hun beschikbaarheid in de periode.

- Kies het tabblad **Chauffeurs** of **Voertuigen** en zoek op naam, code of kenteken.
- Achter elke naam staat een balkje over de periode: rode stukken zijn niet beschikbaar, grijze
  stukken buiten werktijd, de stippen zijn de ritten.
- **Sleep een chauffeur** naar een rit in *Transportritten* of naar een balk in de planner om hem aan die
  rit toe te voegen.

### Beschikbaarheid beheren

Klik op een naam om rechts het detailpaneel te openen.

- **Niet beschikbaar in deze periode** toont verlof, ziekte, training, onderhoud en keuringen.
  Met **+** voeg je er een toe, met het potlood wijzig of verwijder je er een. Je kiest de
  reden, de begin- en eindtijd en eventueel **Elke week herhalen** op gekozen weekdagen tot een
  datum.
- **Werktijden**: vink per weekdag aan of de chauffeur of het voertuig werkt en vul de begin- en
  eindtijd in. Een dag zonder vinkje heeft geen werktijden. Klik op **Werktijden opslaan**.

Na elke wijziging controleert OpenTMS de ritten opnieuw: een rit die nu op een niet-beschikbare
periode of buiten werktijd valt, krijgt een waarschuwing.

Je hebt het recht **Beschikbaarheid van middelen** nodig om dit paneel te zien, en
**Beschikbaarheid en werktijden van middelen beheren** om iets te wijzigen.
