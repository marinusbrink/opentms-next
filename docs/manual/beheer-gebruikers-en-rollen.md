# Beheer — Gebruikers en rollen

> **Vertaling:** deze pagina is de bronversie (Nederlands). De Engelse vertaling
> wordt gegenereerd als onderdeel van de documentatiestap na deze draft.

---

## Wat kun je hier doen?

In de **Beheer**-app stel je in wie toegang heeft tot OpenTMS en wat ze mogen
doen. Je maakt gebruikers aan, wijst hun rollen toe, stelt wachtwoorden opnieuw
in en beheert de rollen zelf. Alleen gebruikers met de juiste rechten zien deze
app.

---

## De Beheer-app openen

1. Klik rechts bovenin op het **wafel-icoon** (negen stippen) om de app-launcher
   te openen.
2. Klik op de tegel **Beheer** (schildicoon, grijze tegel).
3. De app opent op de **Gebruikers**-weergave. Links zie je het navigatiepaneel
   met **Gebruikers** en **Rollen**.

<!-- SCREENSHOT: wafel-launcher met de Beheer-tegel zichtbaar -->

---

## Het navigatiepaneel

Aan de linkerzijde van de Beheer-app staat het **navigatiepaneel** met de
weergaven **Gebruikers** en **Rollen**. De actieve weergave heeft een blauwe
achtergrond met wit icoon en witte tekst.

<!-- SCREENSHOT: de Beheer-app met het navigatiepaneel uitgevouwen en Gebruikers actief -->

**Wisselen van weergave:** klik op **Gebruikers** of **Rollen** in het paneel.

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
   **Sluiten**.
2. Vul het formulier in:
   - **Gebruikersnaam** (verplicht) — uniek binnen uw organisatie.
   - **E-mailadres** (verplicht) — geldig e-mailformaat.
   - **Voornaam** en **achternaam**.
   - **Wachtwoord** (verplicht) — moet voldoen aan het wachtwoordbeleid
     (minimumlengte en complexiteit staan bij het veld vermeld).
   - **Rollen** — selecteer een of meer rollen via de keuzelijst.
3. Klik op **Opslaan** (of druk op **Enter**).

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

**Gebruiker deactiveren:** haal het vinkje bij **Actief** weg. Een inactieve
gebruiker kan niet meer inloggen maar blijft zichtbaar in de lijst.

**Rollen aanpassen:** gebruik de rollenkeuzelijst om rollen toe te voegen of
te verwijderen. Je kunt de beheerdersrol niet verwijderen van de enige
gebruiker die deze heeft — het systeem meldt dat dan.

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
toe aan gebruikers via het gebruikersformulier (zie hierboven).

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
| Rollenlijst bekijken | Rollen beheren |
| Rol aanmaken | Rollen aanmaken |
| Rol bewerken | Rollen bewerken |
| Rol verwijderen | Rollen verwijderen |
| Meerdere rollen verwijderen | Rollen verwijderen (meerdere) |

Als je een weergave wel kunt openen maar een melding "U heeft geen toestemming
voor deze weergave" verschijnt, neem dan contact op met uw beheerder.
