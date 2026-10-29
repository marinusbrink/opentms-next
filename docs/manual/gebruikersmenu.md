# Het gebruikersmenu

> **Vertaling:** deze pagina is de bronversie (Nederlands). De Engelse vertaling
> wordt gegenereerd als onderdeel van de documentatiestap na deze draft.

---

## Wat kun je hier doen?

Rechtsboven in de blauwe balk staat je profielfoto (of je initialen als je geen foto hebt),
met daarnaast de naam van de organisatie waarin je werkt. Klik erop en het gebruikersmenu
gaat open. Daarin:

- zie je je naam, je e-mailadres en de organisatie waarin je nu werkt;
- stap je over naar een andere organisatie waar je met hetzelfde e-mailadres een account hebt;
- open je **Mijn instellingen**, **Profielfoto wijzigen** en **Wachtwoord wijzigen**;
- meld je je af met **Uitloggen**.

<!-- SCREENSHOT: het geopende gebruikersmenu met foto, naam, "Overstappen naar" en de menu-items -->

---

## Overstappen naar een andere organisatie

Heb je met je e-mailadres ook een account bij een andere organisatie (of bij de host), dan
staan die organisaties in het menu onder **Overstappen naar**. Je ziet alleen de organisaties
waar jouw e-mailadres een account heeft.

1. Klik op de organisatie.
2. Is je wachtwoord daar hetzelfde als het wachtwoord waarmee je je aanmeldde, dan stap je
   meteen over.
3. Staat er een slotje achter de naam, dan heb je daar een ander wachtwoord. Er verschijnt
   een venster: vul het wachtwoord van die organisatie in en klik op **Overstappen**.
4. OpenTMS laadt opnieuw, nu in de andere organisatie.

Een wachtwoord dat je bij het overstappen invult, onthoudt OpenTMS tot je uitlogt: stap
je terug en daarna nog eens over, dan wordt er niet opnieuw om gevraagd.

### Wachtwoord onthouden

Vink in het venster **Wachtwoord onthouden** aan als je niet elke keer het wachtwoord wilt
invullen. OpenTMS bewaart je wachtwoord niet: het onthoudt alleen dat jij beide accounts
bezit. De volgende keer stap je zonder wachtwoord over, ook na opnieuw aanmelden, en ook de
andere kant op.

Dit stopt vanzelf zodra een van beide wachtwoorden wordt gewijzigd; daarna vraagt OpenTMS
weer één keer om het wachtwoord. Wil je het zelf stoppen, open dan **Mijn instellingen** en
klik bij de organisatie op **Niet meer onthouden**.

Een verkeerd wachtwoord telt mee als mislukte aanmeldpoging bij die organisatie. Na te veel
pogingen wordt dat account tijdelijk vergrendeld.

Terwijl je bent ingelogd als een andere gebruiker (zie [Tenants en edities](beheer-tenants-en-edities.md)),
kun je niet overstappen; ga eerst terug naar je eigen account.

<!-- SCREENSHOT: het venster "Overstappen naar …" met wachtwoord en Wachtwoord onthouden -->

---

## Inloggen bij een tenant (Customer Success Manager)

Ben je aan de hostkant aangemeld en heb je de rol **Customer Success Manager** (of het recht
**Inloggen als tenantgebruiker**), dan staat in het menu ook **Inloggen bij een tenant…**.

1. Klik op **Inloggen bij een tenant…**. Er opent een lijst met alle tenants.
2. Zoek de tenant met het zoekveld boven de lijst; de lijst laadt verder terwijl je scrolt.
3. Klik op de tenant en klik op **Volgende** (of dubbelklik op de tenant).
4. Kies de gebruiker en klik op **Inloggen als**. Vanaf hier werkt het zoals beschreven bij
   [Inloggen als een gebruiker van een tenant](beheer-tenants-en-edities.md#inloggen-als-een-gebruiker-van-een-tenant):
   een gele balk bovenin laat zien als wie je bent ingelogd, met **Terug naar host**.

<!-- SCREENSHOT: het venster "Inloggen bij een tenant" met zoekveld en lijst -->

---

## Mijn instellingen

In **Mijn instellingen** wijzig je je **voornaam**, **achternaam**, **telefoonnummer** en
**taal**. Je e-mailadres zie je wel, maar alleen een beheerder kan het wijzigen.

Bij **Taal** kies je **Nederlands**, **English** of **Taal van de browser**. De taal geldt
direct na **Opslaan**, en op elke computer waarop je je aanmeldt.

Onderaan staan de organisaties waarvan OpenTMS je wachtwoord onthoudt, met
**Niet meer onthouden**.

<!-- SCREENSHOT: het venster Mijn instellingen -->

---

## Profielfoto wijzigen

1. Kies **Profielfoto wijzigen**.
2. Sleep een afbeelding op de foto, of klik erop om een bestand te kiezen (PNG, JPEG of WebP,
   maximaal 2 MB). Met de rechtermuisknop kies je **Foto verwijderen**.
3. Klik op **Opslaan**.

Je foto verschijnt rechtsboven en in de lijst **Gebruikers**. Een beheerder kan je foto ook
wijzigen in het venster van je gebruiker (zie [Gebruikers en rollen](beheer-gebruikers-en-rollen.md));
ook die foto zie je rechtsboven.

---

## Wachtwoord wijzigen

1. Kies **Wachtwoord wijzigen**.
2. Vul je **huidige wachtwoord** in, daarna twee keer je **nieuwe wachtwoord**.
3. Klik op **Wachtwoord wijzigen**.

Het nieuwe wachtwoord moet voldoen aan de wachtwoordregels van je organisatie; voldoet het
niet, dan zegt het venster waarom. Het wijzigen geldt alleen voor je account in deze
organisatie. Een onthouden overstap naar of van dit account stopt, omdat het wachtwoord is
gewijzigd.
