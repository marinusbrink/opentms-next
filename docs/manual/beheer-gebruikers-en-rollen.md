# Beheer — Gebruikers, rollen en organisatie-eenheden

> **Vertaling:** deze pagina is de bronversie (Nederlands). De Engelse vertaling
> wordt gegenereerd als onderdeel van de documentatiestap na deze draft.

---

## Wat kun je hier doen?

In de **Beheer**-app stel je in wie toegang heeft tot OpenTMS en wat ze mogen
doen. Je maakt gebruikers aan, wijst hun rollen toe, stelt wachtwoorden opnieuw
in en beheert de rollen zelf. Daarnaast bouw je er de organisatie-eenheden op
(vestigingen, afdelingen, teams) en koppel je gebruikers aan die eenheden. Alleen
gebruikers met de juiste rechten zien deze app.

---

## De Beheer-app openen

1. Klik rechts bovenin op het **wafel-icoon** (negen stippen) om de app-launcher
   te openen.
2. Klik op de tegel **Beheer** (schildicoon, grijze tegel).
3. De app opent op de **Gebruikers**-weergave. Links zie je het navigatiepaneel
   met **Gebruikers**, **Rollen**, **Organisatie-eenheden** en de groep
   **Auditlogboeken** met het functioneel en het technisch logboek (beschreven in
   [Beheer — Logboek](beheer-logboek.md)).

<!-- SCREENSHOT: wafel-launcher met de Beheer-tegel zichtbaar -->

---

## Het navigatiepaneel

Aan de linkerzijde van de Beheer-app staat het **navigatiepaneel** met de
weergaven **Gebruikers**, **Rollen** en **Organisatie-eenheden**. De actieve
weergave heeft een blauwe achtergrond met wit icoon en witte tekst.

<!-- SCREENSHOT: de Beheer-app met het navigatiepaneel uitgevouwen en Gebruikers actief -->

**Wisselen van weergave:** klik op **Gebruikers**, **Rollen** of
**Organisatie-eenheden** in het paneel.

**Paneel inklappen:** klik op het pijltje-links bovenaan het paneel (inklapknop).
Het paneel wordt smal en toont alleen de iconen. Beweeg de muis over een icoon om
de naam te zien.

**Paneel uitvouwen:** klik op het pijltje-rechts bovenaan het smalle paneel.

> **Let op:** de inklapstand wordt niet onthouden. Na een pagina-herlaad staat
> het paneel altijd uitgevouwen.

> **Smal scherm:** op schermen smaller dan 768 px toont het paneel automatisch
> alleen iconen. Je kunt het alsnog uitvouwen via het pijltje-rechts.

<!-- SCREENSHOT: het navigatiepaneel ingeklapt (alleen iconen) -->

---

## Gebruikers

### De gebruikerslijst lezen

Het scherm toont een lijst van alle gebruikers van uw organisatie met kolommen
voor gebruikersnaam, e-mailadres, volledige naam, rollen en of de gebruiker
actief is.

<!-- SCREENSHOT: het Gebruikers-scherm met een gevulde lijst -->

**Zoeken:** typ in het zoekveld bovenaan de lijst. De lijst filtert direct op
gebruikersnaam of e-mailadres.

**Sorteren:** klik op een kolomkop om te sorteren; klik nogmaals om de
volgorde om te keren.

**Kolommen aan-/uitzetten:** klik op **Kolommen** om te kiezen welke kolommen
zichtbaar zijn. Met **Vernieuwen** haal je de lijst opnieuw op.

Je kolomkeuze, kolombreedtes en sortering worden per gebruiker bewaard, voor elke
lijst apart. Lukt het bewaren een keer niet, dan verschijnt een melding; je huidige
weergave blijft gewoon staan tot je het scherm verlaat.

**Een rij openen:** dubbelklik op een rij (of druk op **Enter** als de rij de focus
heeft) om de gebruiker te openen. Heb je geen bewerkrecht, dan opent het venster
alleen-lezen.

**Acties op een rij:** klik met de **rechtermuisknop** op een rij voor het
snelmenu met **Bewerken**, **Wachtwoord opnieuw instellen** en **Verwijderen**.
Selecteer eerst meerdere rijen om ze in één keer te verwijderen.

---

### Een nieuwe gebruiker aanmaken

> Vereist: het recht **Gebruikers aanmaken**.

1. Klik op **Nieuwe gebruiker** in de actiebalk boven de lijst. Het venster opent
   met een gekleurde titelbalk; direct eronder staan de knoppen **Opslaan** en
   **Sluiten**, met rechts de tabbladen **Algemeen** en **Identificatie** (het
   vingerafdruk-icoon). Op **Identificatie** staat het technische id van het record
   en wanneer het is aangemaakt; bij een nieuw record is dat tabblad nog leeg.
2. Vul het vak **Account** in:
   - **Profielfoto** — sleep een PNG-, JPEG- of WebP-bestand (maximaal 2 MB) op
     de cirkel, of klik erop om een bestand te kiezen. Zonder foto toont OpenTMS
     de initialen.
   - **Voornaam** en **achternaam**.
   - **E-mailadres** (verplicht) — geldig e-mailformaat.
   - **Telefoonnummer**.
   - **Gebruikersnaam** (verplicht) — uniek binnen uw organisatie.
3. Stel in het vak **Toegang** de aanmelding in:
   - **Wachtwoord** (verplicht) — moet voldoen aan het wachtwoordbeleid, of vink
     **Willekeurig wachtwoord instellen** aan; OpenTMS kiest dan zelf een
     wachtwoord dat aan het beleid voldoet. Stel het daarna opnieuw in via
     [Wachtwoord opnieuw instellen](#wachtwoord-opnieuw-instellen), of laat de
     gebruiker "wachtwoord vergeten" gebruiken.
   - **Moet wachtwoord wijzigen bij volgende aanmelding** — de gebruiker krijgt
     bij de eerstvolgende aanmelding eerst het scherm om een nieuw wachtwoord te
     kiezen.
   - **Account vergrendelen na mislukte aanmeldingen** — standaard aan. Na een
     aantal verkeerde wachtwoorden wordt het account tijdelijk vergrendeld; wie
     dan probeert aan te melden, ziet de melding dat het account is vergrendeld
     en dat hij contact moet opnemen met de lokale IT-ondersteuning.
4. Open het tabblad **Rollen** en vink de rollen aan die de gebruiker krijgt.
   De lijst toont alle rollen van uw organisatie.
5. Open het tabblad **Organisatie-eenheden** en vink de eenheden aan waarvan de
   gebruiker lid is. De eenheden staan als boom; klap een eenheid uit met het
   pijltje om de sub-eenheden te zien. Een vinkje geldt alleen voor die eenheid,
   niet automatisch voor de sub-eenheden. Dit tabblad zie je alleen met het
   recht **Organisatie-eenheden beheren**.
6. Klik op **Opslaan** (of druk op **Enter**). Een gekozen profielfoto wordt bij
   het opslaan geüpload.

Het venster sluit, rechtsonder verschijnt kort de melding "Gebruiker opgeslagen"
en de gebruiker staat in de lijst.

<!-- SCREENSHOT: het formulier "Nieuwe gebruiker" met ingevulde velden -->

**Foutmeldingen:** een verplicht veld dat je leeg laat, krijgt een melding onder
het veld zodra je het verlaat of op **Opslaan** klikt. Een melding van de server
(bijvoorbeeld: gebruikersnaam al in gebruik, of het wachtwoord voldoet niet aan
het beleid) verschijnt bovenin het venster; je invoer blijft staan.

**Wijzigingen weggooien:** sluit je een venster met niet-opgeslagen wijzigingen
(met **Sluiten**, het kruisje of **Escape**), dan vraagt OpenTMS eerst
"Wijzigingen niet opslaan?". Klikken naast het venster sluit het nooit.

---

### Een gebruiker bewerken

> Vereist: het recht **Gebruikers bewerken**.

1. Dubbelklik op de rij van de gebruiker, of kies **Bewerken** in het snelmenu
   (rechtermuisknop).
2. Pas de gewenste velden aan. Het wachtwoord is niet aanpasbaar via dit
   formulier — zie [Wachtwoord opnieuw instellen](#wachtwoord-opnieuw-instellen).
3. Klik op **Opslaan**.

<!-- SCREENSHOT: het formulier "Gebruiker bewerken" -->

**Gebruiker deactiveren:** haal in het vak **Toegang** het vinkje bij **Actief**
weg. Een inactieve gebruiker kan niet meer inloggen maar blijft zichtbaar in de
lijst.

**Profielfoto wijzigen of verwijderen:** sleep een nieuwe foto op de cirkel, of
klik er met de rechtermuisknop op en kies **Foto wijzigen** of **Foto
verwijderen**. Sla daarna op.

**Rollen aanpassen:** vink op het tabblad **Rollen** rollen aan of uit. Je kunt
de beheerdersrol niet verwijderen van de enige gebruiker die deze heeft — het
systeem meldt dat dan.

**Organisatie-eenheden aanpassen:** vink op het tabblad **Organisatie-eenheden**
eenheden aan of uit. Dezelfde koppeling kun je ook vanuit de eenheid zelf
beheren (zie [Leden van een eenheid beheren](#leden-van-een-eenheid-beheren)).

---

### Wachtwoord opnieuw instellen

> Vereist: het recht **Wachtwoord opnieuw instellen**.

1. Kies **Wachtwoord opnieuw instellen** in het snelmenu (rechtermuisknop) van
   de gebruiker.
2. Voer een nieuw wachtwoord in en klik op **Wachtwoord opnieuw instellen**.

Het nieuwe wachtwoord is direct actief. De gebruiker ontvangt geen automatische
melding — informeer hen zelf over het nieuwe wachtwoord.

<!-- SCREENSHOT: het dialoogvenster "Wachtwoord opnieuw instellen" -->

---

### Een vergrendeld account ontgrendelen

> Vereist: het recht **Gebruikersaccounts ontgrendelen**.

Na te veel mislukte aanmeldingen is een account tijdelijk vergrendeld (zie
**Account vergrendelen na mislukte aanmeldingen** hierboven). Wil je niet
wachten tot de vergrendeling vanzelf afloopt:

1. Kies **Gebruikersaccount ontgrendelen** in het snelmenu (rechtermuisknop)
   van de gebruiker. Dit menu-item staat er alleen zolang het account
   vergrendeld is.
2. De melding "Gebruikersaccount ontgrendeld" verschijnt; de gebruiker kan
   direct weer aanmelden.

Via **Kolommen** kun je de kolom **Vergrendeld** tonen om vergrendelde accounts
in de lijst te zien.

---

### Een gebruiker verwijderen

> Vereist: het recht **Gebruikers verwijderen**.

1. Kies **Verwijderen** in het snelmenu (rechtermuisknop) van de gebruiker.
2. Het bevestigingsvenster noemt de gebruiker: "Gebruiker jansen verwijderen?
   Dit kan niet ongedaan worden gemaakt." Klik op **Verwijderen**.

De gebruiker wordt direct en permanent verwijderd. Je kunt je eigen account
niet verwijderen, en de enige gebruiker met de beheerdersrol ook niet — het
systeem meldt dat dan.

---

### Meerdere gebruikers tegelijk verwijderen

> Vereist: het recht **Meerdere gebruikers verwijderen**.

1. Vink de selectievakjes aan naast de gebruikers die je wilt verwijderen, of
   gebruik het selectievakje in de kolomkop om alle zichtbare rijen te
   selecteren.
2. De actiebalk boven de lijst toont het aantal geselecteerde gebruikers en de
   knop **Geselecteerden verwijderen** wordt actief. Klik op
   **Geselecteerden verwijderen**.
3. Het venster **Gebruikers verwijderen** noemt het aantal en waarschuwt dat dit
   niet ongedaan te maken is. Klik op **Verwijderen**.
4. Na afloop toont het venster hoeveel gebruikers zijn verwijderd. Als sommige
   gebruikers overgeslagen zijn (bijv. eigen account of laatste beheerder),
   staan ze met de reden vermeld. Na **Sluiten** verschijnt de melding
   "3 gebruikers verwijderd" en is de lijst bijgewerkt.

Je kunt ook meerdere rijen selecteren en **Verwijderen** kiezen in het snelmenu.

<!-- SCREENSHOT: het dialoogvenster voor verwijdering met het resultaatscherm -->

---

## Rollen

Rollen bepalen wat een gebruiker mag zien en doen in OpenTMS. Je wijst rollen
toe aan gebruikers op het tabblad **Rollen** van het gebruikersformulier (zie
hierboven).

### De rollenlijst lezen

Klik op **Rollen** in het navigatiepaneel links. Je ziet de naam van elke rol,
of het de standaard-aanmeldrol is, of hij zichtbaar is voor gebruikers, en aan
hoeveel gebruikers hij is toegewezen.

**Systeemrollen** (vinkje in de kolom **Systeemrol**) zijn ingebouwde rollen die
je niet kunt bewerken of verwijderen; dubbelklikken opent ze alleen-lezen.

<!-- SCREENSHOT: het Rollen-scherm met de rollenlijst -->

---

### Een nieuwe rol aanmaken

> Vereist: het recht **Rollen aanmaken**.

1. Klik op **Nieuwe rol** in de actiebalk boven de lijst.
2. Vul het formulier in:
   - **Rolnaam** (verplicht, maximaal 256 tekens).
   - **Standaard**: schakel in als elke nieuwe gebruiker automatisch deze rol
     krijgt.
   - **Openbaar**: schakel in als de rol zichtbaar is in de rollenkeuzelijst
     voor gebruikers (bijv. bij zelfregistratie).
3. Klik op **Opslaan**.

<!-- SCREENSHOT: het formulier "Nieuwe rol" -->

---

### Een rol bewerken

> Vereist: het recht **Rollen bewerken**.

1. Dubbelklik op de rij van de rol, of kies **Bewerken** in het snelmenu
   (rechtermuisknop).
2. Pas de naam, standaard-instelling of zichtbaarheid aan.
3. Klik op **Opslaan**.

Systeemrollen kunnen niet worden bewerkt; **Bewerken** in het snelmenu is dan
uitgeschakeld en legt uit waarom.

---

### Een rol verwijderen

> Vereist: het recht **Rollen verwijderen**.

1. Kies **Verwijderen** in het snelmenu (rechtermuisknop) van de rol.
2. Bevestig in het venster "Rol verwijderen".
3. Is de rol aan gebruikers toegewezen, dan volgt een tweede bevestiging met de
   waarschuwing "De rol '{rolnaam}' is toegewezen aan {N} gebruikers.
   Verwijderen verwijdert de rol bij alle gebruikers." Klik nogmaals op
   **Verwijderen**.

Systeemrollen kunnen niet worden verwijderd; **Verwijderen** in het snelmenu is
dan uitgeschakeld.

<!-- SCREENSHOT: de waarschuwing bij verwijdering van een rol met gebruikers -->

---

### Meerdere rollen tegelijk verwijderen

> Vereist: het recht **Rollen verwijderen (meerdere)**.

1. Vink de selectievakjes aan naast de rollen die je wilt verwijderen, of
   gebruik het selectievakje in de kolomkop om alle zichtbare rijen te
   selecteren.
2. De actiebalk boven de lijst toont het aantal geselecteerde rollen en de
   knop **Geselecteerden verwijderen** wordt actief. Klik op
   **Geselecteerden verwijderen**.
3. Het venster **Rollen verwijderen** noemt het aantal en waarschuwt dat dit
   niet ongedaan te maken is. Klik op **Verwijderen**.
   De geselecteerde rollen worden verwijderd. Rollen die aan gebruikers zijn
   toegewezen, worden automatisch van die gebruikers verwijderd.
4. Na afloop toont het scherm hoeveel rollen zijn verwijderd. Als sommige
   rollen zijn overgeslagen, staan ze met de reden vermeld.

<!-- SCREENSHOT: de actiebalk met geselecteerde rollen en het dialoogvenster voor bulkverwijdering met resultaatscherm -->

**Systeemrollen worden overgeslagen.** Heb je een systeemrol geselecteerd?
Die wordt niet verwijderd en verschijnt in de lijst met overgeslagen items —
de overige geselecteerde rollen worden wél verwijderd.

---

## Organisatie-eenheden

Organisatie-eenheden zijn de boomstructuur van uw organisatie: bijvoorbeeld een
hoofdkantoor met daaronder vestigingen, en daaronder afdelingen of teams. Je
koppelt gebruikers aan een eenheid; zo is vastgelegd bij welke vestiging of
afdeling iemand hoort.

### De boom lezen

Klik op **Organisatie-eenheden** in het navigatiepaneel links. Je ziet de
eenheden op het hoogste niveau. Een eenheid met sub-eenheden heeft een
pijltje voor de naam: klik erop om de sub-eenheden uit te klappen. Per eenheid
zie je het aantal **leden** (gekoppelde gebruikers) en wanneer de eenheid is
aangemaakt. Via **Kolommen** kun je ook het **Pad** (de namen van alle
bovenliggende eenheden) en de technische **Code** tonen.

<!-- SCREENSHOT: het scherm Organisatie-eenheden met een uitgeklapte boom -->

**Zoeken:** typ in het zoekveld bovenaan de lijst. De boom maakt plaats voor een
platte lijst van alle eenheden waarvan de naam de zoektekst bevat, op elk
niveau. Zet de kolom **Pad** aan om te zien waar een gevonden eenheid in de boom
zit. Maak het zoekveld leeg om de boom terug te krijgen.

**Een eenheid openen:** dubbelklik op een rij (of druk op **Enter** als de rij de
focus heeft). Heb je geen bewerkrecht, dan opent het venster alleen-lezen.

**Acties op een rij:** klik met de **rechtermuisknop** op een eenheid voor het
snelmenu met **Bewerken**, **Nieuwe sub-eenheid** en **Verwijderen**.

---

### Een nieuwe organisatie-eenheid aanmaken

> Vereist: het recht **Organisatie-eenheden aanmaken**.

1. Klik op **Nieuwe organisatie-eenheid** in de actiebalk boven de lijst, of
   klik met de rechtermuisknop op een bestaande eenheid en kies
   **Nieuwe sub-eenheid** (de bovenliggende eenheid is dan al ingevuld).
2. Vul de **Naam** in (verplicht, maximaal 128 tekens). Twee eenheden onder
   dezelfde bovenliggende eenheid kunnen niet dezelfde naam hebben.
3. Kies de **Bovenliggende eenheid** in de keuzelijst: een boom waarin je
   eenheden uit- en inklapt met het pijltje. Kies **Geen bovenliggende eenheid
   (hoogste niveau)** voor een eenheid op het hoogste niveau. Bestaat de
   bovenliggende eenheid nog niet, dan opent **Nieuwe organisatie-eenheid
   toevoegen…** onderaan de keuzelijst het venster voor een nieuwe eenheid
   bovenop dit venster; na **Opslaan** is de nieuwe eenheid de bovenliggende
   eenheid. Je hebt daarvoor het recht nodig om organisatie-eenheden aan te maken.
4. Klik op **Opslaan** (of druk op **Enter**).

Het venster sluit, rechtsonder verschijnt kort de melding "Organisatie-eenheid
opgeslagen" en de eenheid staat in de boom.

<!-- SCREENSHOT: het formulier "Nieuwe sub-eenheid" met de keuzelijst voor de bovenliggende eenheid -->

---

### Een organisatie-eenheid bewerken

> Vereist: het recht **Organisatie-eenheden bewerken**.

1. Dubbelklik op de eenheid, of kies **Bewerken** in het snelmenu.
2. Pas de **Naam** aan op het tabblad **Algemeen**.
3. Klik op **Opslaan**.

**Een eenheid verplaatsen:** kies op het tabblad **Algemeen** een andere
**Bovenliggende eenheid** en klik op **Opslaan**. De eenheid verhuist met al
haar sub-eenheden en leden. De eenheid zelf en haar sub-eenheden zijn in de
keuzelijst grijs: een eenheid kan niet onder zichzelf komen te staan.

Het venster heeft drie tabbladen: **Algemeen** (de naam en de bovenliggende
eenheid), **Leden** (zie hieronder) en **Identificatie** (het vingerafdruk-icoon
met het technische id).

---

### Leden van een eenheid beheren

> Vereist: het recht **Leden van organisatie-eenheden beheren**. Zonder dit recht
> zie je de ledenlijst wel, maar niet de knoppen om leden toe te voegen of te
> verwijderen.

Open de eenheid en klik op het tabblad **Leden**. Je ziet de gebruikers die
aan deze eenheid zijn gekoppeld, met gebruikersnaam, e-mailadres, naam en of de
gebruiker actief is.

> **Wijzigingen in de leden worden pas toegepast als je op Opslaan klikt.**
> Sluit je het venster zonder op te slaan, dan vraagt OpenTMS "Wijzigingen niet
> opslaan?" en blijven de leden zoals ze waren.

<!-- SCREENSHOT: het tabblad Leden van een organisatie-eenheid -->

**Leden toevoegen:**

1. Klik op **Leden toevoegen** boven de ledenlijst.
2. Het venster **Leden toevoegen aan {eenheid}** toont alle gebruikers die nog
   geen lid zijn. Klik op een rij om die te selecteren (nogmaals klikken maakt de
   selectie ongedaan), of gebruik de selectievakjes. Met het zoekveld vind je
   een gebruiker snel.
3. Klik op **Geselecteerden toevoegen (N)**; de knop toont het aantal.
4. De gekozen gebruikers staan nu in de ledenlijst. Klik op **Opslaan** om de
   lidmaatschappen vast te leggen.

<!-- SCREENSHOT: het venster Leden toevoegen met een selectie -->

**Leden verwijderen:**

1. Klik op het prullenbak-icoon aan het einde van de rij, of selecteer een of
   meer rijen en kies **Verwijderen** in het snelmenu (rechtermuisknop). Er
   volgt geen bevestigingsvraag: zolang je niet hebt opgeslagen, kun je het
   venster sluiten zonder op te slaan.
2. Klik op **Opslaan**.

De gebruiker blijft bestaan; alleen de koppeling met de eenheid verdwijnt.

---

### Een organisatie-eenheid verwijderen

> Vereist: het recht **Organisatie-eenheden verwijderen**.

1. Kies **Verwijderen** in het snelmenu (rechtermuisknop) van de eenheid.
2. Bevestig in het venster "Organisatie-eenheid verwijderen".
3. Heeft de eenheid nog sub-eenheden of leden, dan volgt een tweede bevestiging
   met de waarschuwing "De eenheid '{naam}' heeft {N} sub-eenheden en {M} leden.
   Verwijderen verwijdert ook de sub-eenheden en alle lidmaatschappen." Klik
   nogmaals op **Verwijderen**.

De eenheid en al haar sub-eenheden worden verwijderd. De gekoppelde gebruikers
blijven bestaan; alleen hun koppeling met deze eenheden verdwijnt.

<!-- SCREENSHOT: de waarschuwing bij verwijdering van een eenheid met sub-eenheden -->

---

## Rechten en zichtbaarheid

Wat je kunt zien en doen hangt af van de rechten die uw beheerder aan uw rol
heeft toegekend:

| Wat je wilt doen | Benodigde recht |
|---|---|
| Gebruikerslijst bekijken | Gebruikers beheren |
| Gebruiker aanmaken | Gebruikers aanmaken |
| Gebruiker bewerken | Gebruikers bewerken |
| Gebruiker verwijderen | Gebruikers verwijderen |
| Meerdere gebruikers verwijderen | Meerdere gebruikers verwijderen |
| Wachtwoord opnieuw instellen | Wachtwoord opnieuw instellen |
| Vergrendeld account ontgrendelen | Gebruikersaccounts ontgrendelen |
| Rollenlijst bekijken | Rollen beheren |
| Rol aanmaken | Rollen aanmaken |
| Rol bewerken | Rollen bewerken |
| Rol verwijderen | Rollen verwijderen |
| Meerdere rollen verwijderen | Rollen verwijderen (meerdere) |
| Organisatie-eenheden bekijken | Organisatie-eenheden beheren |
| Organisatie-eenheid aanmaken | Organisatie-eenheden aanmaken |
| Organisatie-eenheid bewerken | Organisatie-eenheden bewerken |
| Organisatie-eenheid verwijderen | Organisatie-eenheden verwijderen |
| Leden toevoegen of verwijderen | Leden van organisatie-eenheden beheren |

Als je een weergave wel kunt openen maar een melding "U heeft geen toestemming
voor deze weergave" verschijnt, neem dan contact op met uw beheerder.
