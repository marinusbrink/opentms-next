# Voertuigen en voertuigcombinaties

De lijsten **Voertuigen** en **Voertuigcombinaties** in de app [Stamgegevens](stamgegevens.md)
bevatten de trekkers, opleggers, bakwagens en bestelwagens waarmee u rijdt, en de vaste koppelingen
van een trekker of bakwagen met zijn oplegger(s). Beide staan in de groep *Wagenpark* van het
navigatiepaneel en zijn per tenant ingeschakeld met de functies *Voertuigen* en
*Voertuigcombinaties* (via de editie van de tenant). De rechten *bekijken*, *aanmaken*, *wijzigen*
en *verwijderen* per lijst bepalen wat een gebruiker mag; zonder het recht om te wijzigen opent een
record alleen-lezen.

## Voertuigen

### De lijst

- Kolommen: naam, kenteken, voertuigtype, soort, eigenaar, standplaats en de combinatie waarin het
  voertuig zit. Brandstof, euroklasse, chassisnummer (VIN) en *Gearchiveerd* kunt u erbij kiezen.
- Zoeken werkt op naam, kenteken en VIN. Bij het kenteken maken spaties, streepjes en hoofdletters
  geen verschil: `12abc3` vindt `12-ABC-3`.
- Filters: **Soort** (trekker, oplegger, bakwagen, bestelwagen), **Eigenaar** en *Toon gearchiveerd*.
- Een voertuig dat u niet meer gebruikt, **archiveert** u (rechtsklikken of in het record).
  Gearchiveerde voertuigen verdwijnen uit de keuzelijsten; dearchiveren zet ze terug.
- Een voertuig dat in een combinatie zit (ook een gearchiveerde), kan niet worden verwijderd. Haal
  het eerst uit de combinatie of verwijder die.

### Het record

Het venster heeft vijf tabbladen.

**Algemeen**

- *Voertuig*: de **naam** (vlootnummer of korte naam, uniek binnen de tenant; hoofdletters en kleine letters tellen niet als verschil, dus "Truck 01" en "truck 01" zijn dezelfde naam), het
  **voertuigtype** en *Gearchiveerd*. De **soort** van het voertuig is die van zijn type; u ziet
  hem als label naast de titel. Kiest u bij een nieuw voertuig een type, dan vult het venster de
  lege velden op het tabblad *Afmetingen en capaciteit* met de standaardwaarden van dat type. Ook
  als u ze leeg laat, neemt het voertuig bij het opslaan de waarden van het type over. Daarna zijn
  het de eigen waarden van het voertuig: een latere wijziging van het type verandert ze niet.
- *Registratie*: **land van registratie**, **kenteken** en **chassisnummer (VIN)**. Een nieuw
  voertuig begint met het standaardland van de tenant. Het kenteken typt u zoals u wilt (met of
  zonder streepjes); het wordt opgeslagen in hoofdletters zonder scheidingstekens en getoond in de
  notatie van het land. Nederlandse kentekens krijgen de streepjes van hun sidecode
  (`12ABC3` wordt `12-ABC-3`); kentekens uit andere landen ziet u zoals opgeslagen. Een kenteken is
  uniek per land van registratie en heeft altijd een land nodig. Het VIN bestaat uit 17 letters en
  cijfers zonder I, O en Q en is uniek binnen de tenant.
- *Eigendom*: de **eigenaar** is een relatie met de rol *Vervoerder* of *Eigen bedrijf*; een nieuw
  voertuig begint met uw eigen bedrijf. De **standplaats** is een relatie met de rol *Depot* of
  *Eigen bedrijf*. Typ in het veld om te zoeken; met **Nieuwe relatie toevoegen…** maakt u de
  relatie meteen aan. Bij *Organisatie-eenheden* kiest u de afdelingen die het voertuig mogen
  gebruiken; leeg betekent alle.
- *Opmerkingen*: vrije tekst.

**Afmetingen en capaciteit**

Lengte, breedte en hoogte (cm), leeggewicht, toegestaan totaalgewicht en maximaal laadvermogen
(kg), maximale EPQ en maximale laadmeters, plus **brandstof** (onder andere diesel, LNG, HVO,
elektrisch, waterstof; *niet van toepassing* voor opleggers) en **euroklasse** (Euro 0 tot en met
Euro 6, EEV, emissievrij).

**Transportkenmerken**

Eén tabel met alle kenmerken van het voertuig. De kenmerken van het voertuigtype staan erin met het
label *van type*; die horen bij het type en kunt u hier niet verwijderen. Daarboven voegt u eigen
kenmerken van dit voertuig toe (bijvoorbeeld een laadklep); die verwijdert u met het
prullenbakje. Verandert u later de kenmerken van het type, dan ziet elk voertuig van dat type de
wijziging meteen.

**Chauffeurs en identificatie**

- *Standaardchauffeurs*: hoogstens vijf chauffeurs die de planner bij dit voertuig voorstelt, in de
  volgorde van de kolom *Volgorde*. Een chauffeur die standaardchauffeur is, kan niet worden
  verwijderd (wel gearchiveerd).
- *Externe identificatienummers*: de nummers van het voertuig in andere systemen (boordcomputer,
  EDI, partners), elk met een bron. Een combinatie van bron en nummer hoort bij één voertuig.

**Recordidentificatie** toont wanneer het record is aangemaakt en gewijzigd.

## Voertuigcombinaties

Een **voertuigcombinatie** is de vaste koppeling die de planner standaard als één eenheid inzet,
bijvoorbeeld trekker 12 met oplegger 34. Welke voertuigen een rit werkelijk hebben gereden, legt
de planning vast bij de rit zelf.

- Een combinatie heeft 2 tot 4 voertuigen. Op positie 1 staat het **gemotoriseerde** voertuig
  (trekker, bakwagen of bestelwagen), op de volgende posities **opleggers**. Het zoekveld boven de
  regels toont daarom eerst alleen gemotoriseerde voertuigen en daarna alleen opleggers. De volgorde
  wijzigt u in de kolom *Positie*.
- Een voertuig zit in hoogstens één **actieve** combinatie. Archiveert u een combinatie, dan komen
  haar voertuigen vrij voor een andere; dearchiveren lukt alleen als geen van de voertuigen
  inmiddels in een andere actieve combinatie zit.
- Laat u de **naam** leeg, dan wordt het de namen van de voertuigen met " + " ertussen
  (bijvoorbeeld `12 + 34`). De naam is uniek binnen de tenant; hoofdletters en kleine letters
  tellen niet als verschil, dus "Nachtdienst" en "NACHTDIENST" zijn dezelfde naam. Alleen de
  schrijfwijze van de eigen naam veranderen mag wel.
- De lijst toont naam, de voertuigen en het gemotoriseerde voertuig; *Gearchiveerd* kunt u erbij
  kiezen.
