# Aanmelden

> **Vertaling:** deze pagina is de bronversie (Nederlands). De Engelse vertaling
> wordt gegenereerd als onderdeel van de documentatiestap na deze draft.

---

## Wat kun je hier doen?

Wie OpenTMS opent en nog niet is aangemeld, komt direct op het aanmeldscherm. Je meldt je
aan met je e-mailadres; een gebruikersnaam gebruik je hier niet. Je hoeft geen organisatie
te kiezen: OpenTMS vindt aan de hand van je e-mailadres en wachtwoord zelf bij welke
organisatie je hoort.

Rechtsboven het scherm kies je de taal: **NL** of **EN**.

---

## Aanmelden

1. Open OpenTMS in je browser. Je komt meteen op het aanmeldscherm.
2. Vul je **e-mailadres** in. Vink **Onthoud mij** aan als je op deze computer aangemeld
   wilt blijven en je e-mailadres de volgende keer niet opnieuw wilt typen.
3. Klik op **Volgende**. Kent OpenTMS het adres niet, dan zegt het scherm dat; controleer
   wat je typte.
4. Vul je **wachtwoord** in en klik op **Inloggen**. Met het oog-icoon zie je wat je typte.

Hoor je bij één organisatie, dan ben je nu binnen.

Heb je **Onthoud mij** aangevinkt, dan begint het scherm de volgende keer direct bij het
wachtwoord, met je e-mailadres erboven. Wil je met een ander adres aanmelden, klik dan op
**Ander e-mailadres**.

<!-- SCREENSHOT: stap 1 met het e-mailadres, Onthoud mij en de knop Volgende -->
<!-- SCREENSHOT: stap 2 met het e-mailadres bovenaan en het wachtwoordveld -->

---

## Je account bestaat bij meer dan één organisatie

Werk je met hetzelfde e-mailadres en wachtwoord bij meerdere organisaties, dan vraagt
OpenTMS na **Inloggen** welke organisatie je bedoelt.

1. Typ de naam van de organisatie, of een deel ervan van minstens drie tekens. Dat mag een
   stuk uit het midden zijn: met **opem** vind je ook **filogic-opemtms**. De volledige naam
   werkt altijd, ook als die kort is.
2. Onder het veld verschijnt **Gevonden:** met de volledige naam zodra precies één van jouw
   organisaties past. Druk op **Tab** om de naam over te nemen.
3. Passen er meer, dan staat er dat je verder moet typen. Past er geen, dan staat dat er ook.
4. Klik op **Doorgaan**.

Er verschijnt nooit een lijst met organisaties: je ziet alleen de naam die past bij wat je
typt, en alleen organisaties waar je wachtwoord klopt. Een beheerder van de host kiest op
dezelfde manier **Host**.

OpenTMS onthoudt welke organisatie je koos. De volgende keer staat die naam al ingevuld, met
**Gevonden:** eronder: druk op **Doorgaan** (of Enter). Wil je een andere organisatie, typ dan
gewoon een andere naam; de ingevulde naam verdwijnt zodra je typt.

Deze stap blijft vijf minuten geldig. Wacht je langer, dan vul je je wachtwoord opnieuw in.

Eenmaal binnen stap je via het gebruikersmenu rechtsboven over naar je andere organisaties,
zonder opnieuw aan te melden (zie [Het gebruikersmenu](gebruikersmenu.md)).

<!-- SCREENSHOT: de stap "Welke organisatie?" met "Gevonden: …" onder het veld -->

---

## Een account aanvragen

Je kunt jezelf niet registreren. Een beheerder van je organisatie maakt je account aan
(zie [Gebruikers en rollen](beheer-gebruikers-en-rollen.md)) en je krijgt dan een e-mail om
je wachtwoord in te stellen.

---

## Wachtwoord vergeten

1. Klik in de wachtwoordstap op **Wachtwoord vergeten?**. Je e-mailadres staat al
   ingevuld.
2. Klik op **Link versturen**. Je kiest geen organisatie: bestaat je account bij meer
   dan één organisatie, dan krijg je voor elke organisatie een eigen e-mail. In de
   e-mail staat voor welke organisatie de link is.
3. Open de e-mail en klik op **Nieuw wachtwoord kiezen**. Dat mag ook op een andere
   computer of telefoon dan waar je de link aanvroeg.
4. Vul je nieuwe wachtwoord twee keer in en klik op **Wachtwoord opslaan**.
5. Klik op **Naar OpenTMS** en log in met je nieuwe wachtwoord. Je e-mailadres staat dan
   al ingevuld.

De link werkt één keer en is 24 uur geldig. Tot je hem gebruikt, blijft je huidige
wachtwoord werken. Was je account vergrendeld na te veel mislukte pogingen, dan is die
vergrendeling opgeheven zodra je een nieuw wachtwoord hebt gekozen.

Kent OpenTMS je e-mailadres niet, dan zegt het scherm dat. Staat het versturen van
e-mail voor je organisatie uit, dan meldt het scherm dat er geen e-mail kon worden
verstuurd; vraag dan je beheerder om een link.

<!-- SCREENSHOT: het scherm "Wachtwoord vergeten?" met het e-mailadres en de knop Link versturen -->
<!-- SCREENSHOT: het scherm "Kies je wachtwoord" met de twee wachtwoordvelden -->

---

## Je beheerder vraagt een nieuw wachtwoord

Heeft je beheerder ingesteld dat je bij de volgende aanmelding een nieuw wachtwoord
moet kiezen, dan gaat OpenTMS na **Inloggen** met je huidige wachtwoord eerst naar
**Kies je wachtwoord**. Kies een nieuw wachtwoord, klik op **Wachtwoord opslaan** en
daarna op **Inloggen**: je logt in met je nieuwe wachtwoord en gaat verder waar je
heen wilde.

---

## Een link om je wachtwoord te kiezen

Je krijgt zo'n link in een uitnodiging voor een nieuw account, als je beheerder een
link stuurt, of na **Wachtwoord vergeten?**. Elke link werkt één keer.

| Link | Geldig |
|---|---|
| Uitnodiging voor een nieuw account | 7 dagen |
| Link van je beheerder of na **Wachtwoord vergeten?** | 24 uur |

Werkt een link niet meer (verlopen of al gebruikt), dan zegt het scherm dat. Klik op
**Nieuwe link aanvragen** om via **Wachtwoord vergeten?** een nieuwe te krijgen, of
vraag je beheerder om een nieuwe uitnodiging.

---

## Lukt aanmelden niet?

- **Er is geen account met dit e-mailadres**: controleer het adres, of vraag je beheerder
  of je account bestaat.
- **Het wachtwoord klopt niet**: probeer het opnieuw. Na te veel mislukte pogingen wordt je
  account tijdelijk vergrendeld. Met **Wachtwoord vergeten?** kies je een nieuw
  wachtwoord; dat heft de vergrendeling ook op.
- **Inloggen lukt nu niet**: een van de omgevingen is tijdelijk niet bereikbaar. Probeer
  het later opnieuw.
- **Wachtwoord vergeten**: zie [Wachtwoord vergeten](#wachtwoord-vergeten).
