<a name="top"></a>

<div align="center">

[![🇩🇪 Deutsch](https://img.shields.io/badge/%F0%9F%87%A9%F0%9F%87%AA_Deutsch-24292f?style=for-the-badge)](#deutsch)
[![🇬🇧 English](https://img.shields.io/badge/%F0%9F%87%AC%F0%9F%87%A7_English-24292f?style=for-the-badge)](#english)

</div>

<a name="deutsch"></a>

# Anleitung: Übungsprojekte und virtuelle Siemens-SPS

Stand: 13.11.2026.

<div align="center">

[![Start](https://img.shields.io/badge/Start-24292f?style=for-the-badge)](#de-start)
[![Profile](https://img.shields.io/badge/Profile-24292f?style=for-the-badge)](#de-profile)
[![Analog](https://img.shields.io/badge/Analog-24292f?style=for-the-badge)](#de-analog)
[![Betrieb](https://img.shields.io/badge/Betrieb-24292f?style=for-the-badge)](#de-betrieb)
[![TIA-Projekte](https://img.shields.io/badge/TIA--Projekte-24292f?style=for-the-badge)](#de-tia-projekte)
[![AT-1B / AT-4B](https://img.shields.io/badge/AT--1B_%2F_AT--4B-24292f?style=for-the-badge)](#de-erweiterungen)
[![Modell](https://img.shields.io/badge/Modell-24292f?style=for-the-badge)](#de-modell)
[![Prüfungen](https://img.shields.io/badge/Pr%C3%BCfungen-24292f?style=for-the-badge)](#de-pruefungen)
[![Bauen](https://img.shields.io/badge/Bauen-24292f?style=for-the-badge)](#de-bauen)

</div>

<a name="de-start"></a>

## Start und Dateien

Benötigt werden Windows x64 und .NET Framework 4.8. Für den Anlagenbetrieb kommen
TIA Portal V18 und S7-PLCSIM Advanced V5.0 Update 2 hinzu. Die Verbindung arbeitet
ausschließlich mit einer bestehenden lokalen virtuellen CPU über PLCSIM-Softbus.

Das vollständige Simulatorpaket an einen beschreibbaren Ort entpacken und
`AT2AT3.Simulator.exe` starten. Im gebauten Quellrepository funktionieren auch
`Start.cmd` und `start.ps1`. Die Anwendung beginnt getrennt mit einer leeren Vorschau.
Das Auswählen oder Speichern eines Profils startet keine Siemens-API.

| Ordner | Inhalt |
|---|---|
| `config/profiles/` | Mitgelieferte Profilvorlagen |
| `profiles/` neben der EXE | Eigene gespeicherte Profile |
| `tia/` | Übungsarchive und Kopie des Referenzprojekts |
| `artifacts/` | Sitzungsberichte; im Quellcheckout im Projektordner, im portablen Paket neben der Anwendung |
| `live-probe/`, `restore-default/` | Eigene Hilfsprogramme im Paket mit Siemens-Adapter |

Zum Umziehen den gesamten Anwendungsordner einschließlich eigener Profile kopieren.
Auf dem Zielrechner müssen die Siemens-Komponenten separat installiert sein;
der dortige CPU-Instanzname wird im Profil eingestellt.

<a name="de-profile"></a>

## Oberfläche und Profile

Oben stehen **Projektprofil**, **Mit SPS verbinden**, **SPS-Verbindung trennen**
und **CPU auf Default zurückstellen**. Links werden Prüfling und Verbindung
bedient. Die Anlage bleibt beim Wechsel zwischen **Bewegung** und **Signale**
sichtbar. **Verbindungsdetails und Ereignisse** blendet zwei zusätzliche Reiter ein.
**Weitere Aktionen** enthält Aufzeichnungsexport und Snapshot.

Die Steuerung kommt ausschließlich aus der virtuellen SPS.

---

<a name="de-profil-einrichten"></a>

### Profil einrichten

1. Vorlage auswählen und **Profil einrichten** anklicken. Bei mitgelieferten
   Vorlagen wird eine eigene Kopie angelegt; eigene Profile lassen sich bearbeiten.
2. Profilnamen und den exakten CPU-Instanznamen aus PLCSIM Advanced eintragen.
   Die Verbindungsart „lokale virtuelle CPU“ ist fest vorgegeben.
3. Die neun digitalen Signale zuordnen. Alle verwenden BOOL. Die Namen der
   Variablen in TIA sind frei wählbar, entscheidend sind die zugeordneten Adressen.
4. Adressen wie `I0.0` oder `Q0.1` übernehmen. Leer bedeutet noch offen.
   Unter **E/A-Bereiche und digitale Geschwindigkeit** die tatsächlich verfügbaren
   Eingangs- und Ausgangsbereichsgrößen aus TIA angeben.
5. **Profil speichern**. Unvollständige Profile lassen sich speichern; verbunden
   werden kann erst mit vollständiger gültiger Zuordnung und den Verbindungsfreigaben.

| Signal | Richtung | Voreinstellung AT-3 / Referenz |
|---|---|---|
| Sensor links | C# → SPS-Eingang | I0.0 |
| Optischer Sensor | C# → SPS-Eingang | I0.1 |
| Sensor rechts | C# → SPS-Eingang | I0.2 |
| Hall-Signal | C# → SPS-Eingang | I0.3 |
| Lampe grün | SPS-Ausgang → C# | Q0.0 |
| Motor rechts | SPS-Ausgang → C# | Q0.1 |
| Motor links | SPS-Ausgang → C# | Q0.2 |
| Lampe gelb | SPS-Ausgang → C# | Q0.3 |
| Lampe rot | SPS-Ausgang → C# | Q0.4 |

Diese Adressen gehören zu den mitgelieferten Projektständen und sind änderbar.
Im AT-2-Profil bleiben sie zunächst offen. Es werden keine fehlenden Adressen ergänzt.
Falsche Richtung, ungültige Bitposition, Bereichsüberschreitung und Überschneidungen
werden abgewiesen. Bei Fehlern bleiben die übrigen Formulareingaben erhalten.

Über **Profile…** können Profile kopiert, importiert und exportiert werden.
Ein Import erzeugt eine eigene Kopie. Ein Profilwechsel setzt die Anlagenvorschau
und die angezeigte Aufzeichnung zurück; er ist erst nach vollständigem Trennen
und Abschluss einer laufenden CPU-Rückstellung möglich.

<a name="de-analog"></a>

### Analoge AT-2-Kopplung: Potentiometer und Motordrehzahl

Im Profil **AT-2 Übung** ist **Analoge SPS-Drehzahl verwenden** voreingestellt.
Unter **Profil einrichten** werden zusätzlich zu den neun digitalen Signalen
**Potentiometer Channel 5 · IW** und **Drehzahlvorgabe Motor · QW** zugeordnet.
Alle AT-2-Adressen bleiben zunächst offen. TIA-Variablennamen sind frei wählbar;
entscheidend sind Adresse, Datentyp INT und Modulkonfiguration.

1. In der eigenen TIA-Arbeitskopie ein AI-Modul mit acht Spannungseingängen
   **−10…+10 V** und ein AQ-Modul mit vier Spannungsausgängen **0…10 V** konfigurieren.
   Die verwendeten E/A-Bereiche müssen dem automatischen Prozessabbild von OB1
   zugeordnet sein; die Kopplung bleibt bei `SingleStep_C`.
2. Die Adresse des Potentiometerkanals und des Motor-AQ aus TIA übernehmen,
   z. B. als `IW…` / `QW…`. Die Bezeichnung „Channel 5“ allein bestimmt keine
   absolute Adresse. Bei Kanalnummer 5 und AI-Startbyte B wäre sie B + 2×5;
   maßgeblich ist die in TIA angezeigte Kanaladresse.
3. Unter **E/A-Bereiche und digitale Geschwindigkeit** die gesamten verfügbaren
   Bereichsgrößen in Bytes eintragen. Das jeweilige Wort muss vollständig darin
   liegen und darf kein anderes Signal überlappen. Eine teilweise Zuordnung
   kann gespeichert werden, ermöglicht aber keine analoge Verbindung.
4. Unter **Analoge Modulbereiche und Anfangsstellung** AI **−10 / +10 V**, AQ
   **0 / 10 V** und die Potentiometer-Anfangsstellung, üblicherweise **0 V**, setzen.
   Unter **Anlagenparameter** die **Geschwindigkeit bei 10 V** und bei Bedarf
   Beschleunigung/Verzögerung einstellen. Dies sind Modellwerte, keine Labormessung.
5. TIA-Programm laden, CPU auf RUN / Default stellen, Profil speichern und verbinden.
   Bei stillstehendem Band ohne aktive Motorbefehle den Prüfling links einsetzen.

Der Regler **Potentiometer – SPS-Board, Channel 5** lässt sich auch bei aktiver
Verbindung bedienen. Er überträgt seinen AI-Wert am nächsten Eingangsabgleich;
er ändert die Motorgeschwindigkeit nicht unmittelbar. Daneben stehen getrennt
der gewünschte Rohwert, der zuletzt übertragene AI-Wert, der tatsächlich aus der
SPS gelesene AQ-Wert, die Ausgangsspannung und die Modellgeschwindigkeit.
Ohne frische Verbindung werden analoge SPS-Werte als nicht verfügbar angezeigt.

Das zweite Potentiometer am **Motoransteuerungsmodul** wird getrennt als fest
auf **0 Ω / linker Anschlag** dargestellt. Eine zusätzliche elektrische Wirkung
wird dafür nicht angenommen.

| Spannung | AI bei ±10 V | AQ bei 0…10 V |
|---|---:|---:|
| −10 V | −27648 | unzulässig |
| −5 V | −13824 | unzulässig |
| 0 V | 0 | 0 |
| 5 V | 13824 | 13824 |
| 10 V | 27648 | 27648 |

Die Übertragung verwendet signierte 16-Bit-Wörter in Siemens-Byte-Reihenfolge.
Der normale Regler bietet 0…10 V; negative AI-Werte sind nur über die getrennte
Diagnose-API prüfbar. Weitere dokumentierte symmetrische AI-Bereiche sind im
Profil wählbar; die Reglerobergrenze folgt dem gewählten Bereich. Für AT-2 ±10 V
verwenden. Der Motor-AQ bleibt auf 0…10 V begrenzt; ungültige Rohwerte führen zum
Abbruch statt zu einer ersetzten Geschwindigkeit.

**MOVE prüfen:** In der eigenen FUP-Lösung den AI-Rohwert per MOVE auf das
zugeordnete AQ-Wort übertragen. Motorfreigaben und Richtungsverriegelung bleiben
Teil der SPS-Lösung. Bei einem Modellwert von 100 mm/s liefern 0, 5 und 10 V nach
der Übernahme 0, 50 und 100 mm/s, sofern eine Richtung freigegeben ist. Bei
aktivierter Motordynamik Beschleunigung und Auslauf abwarten. AI und AQ zusätzlich
in der TIA-Beobachtungstabelle vergleichen. Vor dem Entnehmen beide Motorbefehle
im SPS-Programm ausschalten; AQ = 0 allein entsperrt die Prüflingsbedienung nicht.

**UNSCALE prüfen:** In der eigenen FUP-Lösung Prozentwerte 35,0 und 60,0 mit
`LO_LIM = 0.0`, `HI_LIM = 100.0` und `BIPOLAR = FALSE` in das AQ-Wort umsetzen.
`RET_VAL` in TIA prüfen. Nur der gewählte MOVE- oder UNSCALE-Pfad darf das AQ-Wort
beschreiben. Erwartet werden ungefähr 9677 bzw. 16589, entsprechend 3,5 bzw. 6 V
und bei 100 mm/s Modellmaximum etwa 35 bzw. 60 mm/s. Die digitalen Ausgänge
bestimmen weiterhin die Richtung. Zähler, Kalibrierung und Messwert bleiben in TIA.

Für den bisherigen digitalen Test die Analogoption in einer Profilkopie ausschalten
und beide Analogadressen leer lassen. Dieser Modus verwendet weiterhin neun
BOOL-Signale und eine feste digitale Modellgeschwindigkeit. Im analogen Profil
wirkt ausschließlich die tatsächliche SPS-AQ-Vorgabe auf die Geschwindigkeit.

MOVE, UNSCALE, Motorsteuerung, Messung und Ampel werden nicht als fertige
Schülerlösung in das AT-2-Startarchiv eingefügt. Die Simulation bildet
die zwei benötigten Analogsignale ab, nicht sämtliche unbenutzten Modulkanäle,
Verdrahtungsfehler, elektrische Kennlinien oder reale Kalibrierabweichungen.

<a name="de-betrieb"></a>

## Anlage betreiben und Messwerte prüfen

1. Programm in TIA vollständig laden; keinen laufenden Ladevorgang unterbrechen.
2. Die gewünschte virtuelle CPU muss in RUN und im PLCSIM-Modus Default sein.
3. Passendes Simulatorprofil wählen. Die drei Freigaben betreffen Siemens-Zugriff,
   CPU-Synchronisierung und das geladene Programm ohne laufenden TIA-Ladevorgang.
4. **Mit SPS verbinden** wählen. Die Verbindung beginnt mit leerem Band.
5. Bei bestätigtem Stillstand **Prüfling einsetzen (links)**. Die SPS reagiert
   gemäß dem Programm auf die Sensorsignale und steuert Motor und Lampen.
6. Bei Stillstand lässt sich der Prüfling an jeder Position entfernen und erneut
   links einsetzen. Die Verbindung bleibt auch nach einem Durchlauf bestehen.
7. Zum Beenden **SPS-Verbindung trennen** wählen.

Ein bereits gesetzter SPS-Motorbefehl wird beim Verbinden nicht überschrieben.
Solange ein Motorbefehl aktiv ist oder das Band noch ausläuft, bleibt das
Einsetzen und Entnehmen gesperrt. Betriebsfreigaben und gespeicherte Ablaufzustände
werden im SPS-Programm beziehungsweise über TIA oder das eigene HMI bedient.
Der Simulator schreibt keine Controller-Merker.

### Bandmarkierung und Kalibrierfahrt

Die helle Bandmarkierung mit Zeigerspitze folgt dem tatsächlichen Bandweg.
Der Maßstab hat Teilstriche alle **10 mm**, die Markenposition wird zusätzlich
auf **0,1 mm** angezeigt. Bei verbundener SPS, Stillstand und ausgeschalteten
Motorbefehlen setzt **Bandmarkierung auf 0** nur die sichtbare Markenreferenz.
Bandweg, Hallphase und SPS-Zähler bleiben erhalten.

Für die Kalibrierübung: Markierung auf 0 setzen, den SPS-Impulszähler über TIA
zurücksetzen und das Band durch die eigene SPS-Logik ungefähr 100 mm nach rechts
fahren. Anhalten und die tatsächlich angezeigte Markenposition sowie den
SPS-Impulszähler notieren. Dreimal wiederholen und den Mittelwert der drei
Verhältnisse Strecke/Impulse bilden. Verlässt die Markierung die sichtbare Skala,
wird dies angezeigt; sie springt nicht automatisch zum Anfang zurück.

Kalibrierung, Messablauf und Hand-/Automatikbetrieb werden in der SPS programmiert.
**Messwert, Hallzähler, Kalibrierfaktor und Messstatus werden du in der
TIA-Beobachtungstabelle geprüft.**

---

<a name="de-freeze-und-laden"></a>

### Trennen, Freeze und erneutes Laden

Die Verbindung synchronisiert die virtuelle CPU mit `SingleStep_C`. Währenddessen
kann die CPU für den Datenaustausch in Freeze stehen. Nach einer eigenen,
ordnungsgemäß beendeten Sitzung gibt der Simulator die Synchronisierung frei und
prüft die Rückkehr zu Default.

Vor erneutem Laden aus TIA die SPS-Verbindung trennen und die bestätigte
Default-Rückmeldung abwarten. Die Simulatoranwendung darf geöffnet bleiben.
Der farbige CPU-Hinweis zeigt den zuletzt bestätigten Zustand, keine unabhängige
laufende Überwachung von Änderungen in TIA.

Falls Freeze zurückbleibt, **CPU auf Default zurückstellen** für die ausgewählte
CPU verwenden.

Unter **Erweiterte Testarten** stehen zusätzlich ein stationärer E/A-Test und ein
begrenzter Förderband-Abnahmetest bereit. Für die normale Bedienung bleibt
**Interaktive Verbindung** ausgewählt.

<a name="de-tia-projekte"></a>

## TIA-V18-Übungsdateien

Die Startarchive sind eigene Rekonstruktionen aus den bereitgestellten Unterlagen,
keine originalen Hochschulvorlagen. Sie wurden nativ erzeugt und mit TIA wiederhergestellt.

<a name="de-at2"></a>

### AT-2

`tia/AT2_Start_Uebung_Originalstand.zap18` über **Projekt → Dearchivieren** in einen
neuen Ordner holen. CPU 1516-3 PN/DP V2.8 ist vorhanden; I/O-Baugruppen fehlen
absichtlich. OB1, FC1/UNSCALE, FC2, leerer FC3, FC6/Testmessung und Zähler-DB bilden
den rekonstruierten Vorbereitungsstand des sechsseitigen Anhangs.

Motorverriegelung, Signalzuordnung, Kalibrierung, Messablauf und Ampel bleiben
Übungsaufgaben. **Acht Übersetzungsfehler wegen noch fehlender globaler Signale sind
beabsichtigt:** zwei in FC2, sechs in FC6. Der vollständige Stand ist noch nicht
lauffähig. Das Simulatorprofil enthält deshalb zunächst keine I/O-Adressen.

Die überlappenden DInt-Adressen MD3/MD5 aus der Vorlage wurden zu MD24/MD20
korrigiert; die Länge als LReal liegt auf M10.0. Bool-Merker bleiben M0.1, M2.5 und
M4.0 bis M4.3. Abgeschnittene Namen wurden vervollständigt.

**Adresskorrektur vom 09.11.2026:** Das Startarchiv enthält für „Länge in mm“ jetzt `%M10.0` mit Datentyp LReal, entsprechend der Versuchsanleitung. Der zuvor fehlerhafte Eintrag `%ML32` ist ersetzt. Die Variable belegt Merkerbyte 10 bis 17.


<a name="de-at3"></a>

### AT-3

`tia/AT3_Start_Uebung_Originalstand.zap18` dearchivieren. CPU V2.8 sowie DI32,
DQ32, AI8 (±10 V) und AQ4 (0–10 V) sind vorhanden. Motorsteuerung und Hallmessung
sind anhand des eigenen Referenzprojekts vorbereitet. Der linke Sensor startet
den gespeicherten Zyklus, der rechte beendet ihn. Hallimpulse im optischen Fenster
liefern die Länge.

**OB1 Netzwerk 5 bleibt für die Ampel leer.** Die Lampenvariablen sind vorbereitet,
die Lampenlogik fehlt absichtlich. Die digitalen Adressen stehen in der Tabelle
zur Profilkonfiguration.

Das HMI ist noch nicht angelegt. Ein KTP1200 Basic PN, `6AV2 123-2MB03-0AX0`,
Geräteversion 14.0.1.0, gemäß Versuchsanleitung selbst hinzufügen. HMI-Bilder,
Animationen, Stopp/Fortsetzen und Hand/Automatik bleiben Übungsaufgaben.

Der optimierte DB3 enthält `HallImpulse` (Int), `StreckeMm` (LReal) und
`MillimeterProImpuls` (LReal, **Testfaktor 1,2**). In der TIA-Beobachtungstabelle
können diese Variablen symbolisch verwendet werden; feste DB-Offsets sind dafür
nicht nötig. Der Testfaktor ersetzt die Kalibrierungsaufgabe nicht.

<a name="de-referenz"></a>

### Referenz und neuer Ausgangszustand

`tia/AT2_AT3_Referenzprojekt.zip` enthält eine vollständige Kopie des eigenen
Referenzprojektordners. Vollständig entpacken, `AT2_AT3_test/AT2_AT3.ap18` öffnen
und als Arbeitskopie speichern. Die ursprüngliche Referenz wurde nicht verändert.
Die Prüfsummen der drei Pakete stehen in `tia/manifest.json`.

Für einen neuen Übungsversuch das unveränderte Startarchiv erneut dearchivieren.
Die Übungs-CPUs V2.8 unterscheiden sich von der Referenz-CPU V3.0. Bei der
Hardwareübersetzung gab es keine Fehler, aber zwei Warnungen zu fehlendem
Zugriffspasswort und ungeschütztem CPU-Display.

<a name="de-erweiterungen"></a>

## Erweiterungen AT-1B und AT-4B

Die Erweiterungen sind eigenständige Vorbereitungen; sie benötigen den C#-Förderbandsimulator nicht.
Originale Laborunterlagen und fertig gelöste Versuchsaufgaben werden nicht mitgeliefert.

### AT-1B: LabVIEW-Messstationen

1. Den vollständigen Repository-/Paketordner zusammenlassen und **`Start_AT1B.cmd`** öffnen.
   Alternativ `extensions/AT1B/Pruefstand/AT1B_Pruefstaende.lvproj` in LabVIEW öffnen.
2. `AT1B_Ultraschall.vi`, `AT1B_Radar.vi` oder `AT1B_Naeherung.vi` wählen und mit
   **Strg+R** bzw. dem Ausführen-Pfeil starten. Fahrt und Aufnahme beginnen erst auf Anforderung.
3. Handposition einstellen und **Position setzen**, oder Start, Ziel, Tempo, Haltezeit,
   Wiederholungen und Fahrart (Pendeln/Schritte/Einfachfahrt) wählen und **Ablauf starten**.
   Pause hält die Fahrt; Stop beendet sie. Ultraschall/Näherung verwenden mm, Radar m.
4. Für Aufnahme **Messung eingeschaltet** und **Aufnahme eingeschaltet** aktivieren.
   Danach einen neuen CSV-Dateipfad wählen und **CSV speichern**. Vorhandene Dateien
   werden nicht überschrieben. Acht leere Auswerteblätter liegen unter `extensions/AT1B/Messprotokolle/`.
5. Mit **Pruefstand beenden** regulär schließen. Ungespeicherte Daten zuerst speichern
   oder ausdrücklich zum Verwerfen freigeben. Aufnahme, CSV, Beenden und weitere Parameter
   liegen weiter unten im Frontpanel; dazu scrollen.

Geprüft: LabVIEW 2026 Q3, 32 Bit, 26.3.1f1. Für die Benutzung sind kein MCP, Python,
DAQ-Gerät oder Siemens erforderlich. Die neun VI-Referenzen sind relativ; native VIs
sind in LabVIEW bearbeitbar, XML-/Formelquellen liegen daneben. Andere LabVIEW-Versionen
wurden nicht geprüft. `extensions/manifest.json` enthält Dateiprüfsummen und Prüfstatus.

- **Ultraschall:** Start ohne Teachpunkte; zunächst `NaN`. „Teach-Taste gedrueckt“
  einschalten, halten, ausschalten. Laut AU004-Handbuch 2–6 s für den Programmiermodus,
  danach zweimal etwa 1 s für P1/P2; über 6 s invertiert. Reset: Programmiermodus,
  Echo ausschalten, kurz bestätigen. Die feste Beispielanzeigeskala bleibt nach erneutem Teach erhalten.
- **Näherung:** NJ8/CJ8 und Probe wählen. Bei „Axial s“ bewegt die Fahrt s, bei
  „Seitlich x“ bewegt sie x; „Andere Achse“ stellt jeweils die zweite Koordinate.
  Nullsetzen verändert nur den Maßstabsbezug. Probenbreite 24/40 mm ist separat;
  Hysterese, Materialfaktoren und die Umrechnung von x nach z selbst auswerten.
- **Radar:** Füllhöhe und Sensorhöhe bestimmen den tatsächlichen Abstand.
  Sensorparameter mit **Sensorabgleich uebernehmen**, Auswertegerät gesondert mit
  **Auswertung uebernehmen** anwenden. Echoverlust/Versorgung aus liefert ungültige Werte und Relais AUS.

**PACTware:** `extensions/AT1B/PACTware/AT1B_Radar_Offline.PW6` mit installiertem
PACTware 6.2 und VEGA-DTM 2.6.2.2 öffnen und als eigene Kopie speichern.
Aufbau: VEGA USB → VEGAMET 391 → VEGAPULS C 20 series HART (Gerätefamilie des C22).
Das jeweilige Gerät doppelt anklicken, um seine Offlineparameter zu öffnen.
Der Katalog zeigt nur zum gewählten übergeordneten Gerät passende Einträge.
Anwendung, Einheiten, Abgleich, Dämpfung, Linearisierung und Trendseite wurden geöffnet;
Speichern und Wiederöffnen einer privaten Parameterkopie wurde geprüft. Ohne Gerät bleibt
die Trendseite ohne Messdaten. Es besteht keine LabVIEW-DTM-/HART-Kopplung.
Der geprüfte VEGAMET-Standardabgleich zeigt mA; digitale HART-Auswertung benötigt laut
Hersteller eine RS232-/Ethernet-Geräteoption. Abgleich in Metern und Relais 2 wurden
in dieser Offline-DTM-Ansicht nicht nachgewiesen (sichtbar: Relais 1 und 6).
Andere Relais oder Einheiten daher nicht als Ersatz der Versuchsvorgabe verwenden.

### Modellannahmen für AT-1B

| Bereich | Grundlage und Grenze |
|---|---|
| AU004 | Datenblatt: 50–400 mm, 4–20 mA, 3 mm Auflösung, 0,3 s Anlauf. Beispielanzeige fest 50–400 mm; analoge Rampe von Blindzone bis P1. Kein Original-Hochschul-VI. |
| Teach/Fehler | Kurzzeitfenster 0,7–1,5 s, 30-s-Timeout und 3-mm-Schalthysterese sind Beispiele. Reset führt zu „nicht konfiguriert“; fehlende/ungültige Messwerte als NaN statt erfundener Fehlerströme. |
| NJ8 | Beispiel: 8 mm Nennabstand, 7,5 % Hysterese; Datenblatt-Materialfaktoren Cu 0,40 / Al 0,42 / Messing 0,50 / V2A 0,72. Keine Kalibrierung eines konkreten Sensors. |
| CJ8 und Seitenkurve | Einstellbare Beispiel-Materialfaktoren und Empfindlichkeit; elliptische Seitenkante, Beispielreichweite 12 mm. NJ8-Magnetfaktor 1,05 ebenfalls nur Beispiel. 24-/40-mm-Probenbreite und beide Achsen wirken getrennt. |
| Elektrik | Ideales 0-/24-V-PNP-Modell ohne Last-, Leitungs- und Leckstromeffekte. |
| Radar | Sensorhöhe 1,5 m ist Beispiel; 60 L belegt keine konkrete Behälterform. Dämpfung erster Ordnung, lineare bzw. liegende Zylindergeometrie als wählbare Varianten; getrennte Strom-/Auswertekette und Relaisfenster. |
| Zeit/Aufnahme | Feste 20-ms-Modellschritte; Windows darf den Ablauf verlangsamen. Aufnahme alle 100 ms Modellzeit, maximal 10.000 Zeilen. Keine Echtzeit- oder Hardwaregleichheitsgarantie. |

Grundlagen: AT-1B-Versuchsanleitung S. 9–22, AU004-Datenblatt/Handbuch sowie
NJ8-18GM50-E2 (282965) und CJ8-18GM-E2 (019462). Die Parametrierung und Auswertung
bleiben Aufgaben des Nutzers. [VEGAMET-391-Handbuch: analoge/digitale Eingänge und Geräteoptionen](https://www.vega.com/api/sitecore/DocumentDownload/Handler?documentContainerId=4478&documentGroupId=36032&fileExtension=pdf&languageId=1&version=03-06-2025).

### AT-4B: Übungsstart und getrennte Technikprüfung

`tia/AT4B_Start_Uebung_Nachbildung_3AN02.zap18` in TIA V18 über **Projekt → Dearchivieren**
in einen neuen kurzen Ordner holen. Enthalten: CPU und leeres FUP-Main/OB1, Simulation
aktiviert. HMI, Variablen, FCs, Rechenoperationen und OB1-Aufrufe selbst projektieren.
Die Adressen aus der Abbildung auf S. 9 stehen separat unter
`extensions/AT4B/Quellenreferenz_Adressen.csv`; sie sind nicht vorab importiert.
LReal `%M2.0`, `%M10.0`, `%M18.0` belegt je acht Bytes; `%M26.0` bleibt getrennt.
Für den abgebildeten Namen `Q1` wurde keine unbelegte Funktion erfunden.

Die Anleitung nennt in einer Fußnote 3AP02; der installierte Katalog enthielt diesen
Typ nicht. Das beigefügte Datenblatt nennt 3AN02. Die eigene Nachbildung verwendet
deshalb **6ES7516-3AN02-0AB0, V2.8**; die Firmware ist eine lokale Katalogwahl.

`tia/AT4B_Technikpruefung_20261113.zap18` ist eine separate technische Referenz ohne
Rechenlösung. Ebenfalls dearchivieren, dann **PLC_1** gezielt in eine lokale virtuelle
S7-1500 im PLCSIM-Softbus laden und auf **RUN / Default** setzen.
Im Nachweis: `AT4B_Verbindungstest_20261110`, CPU-X1 `192.168.0.78`.
**HMI_2 [KTP1200 Basic PN] → Online → Simulation → Starten** verwenden, ohne zusätzlichen
Variablensimulator. Beide Eingabefelder schreiben LReal nach M2/M10, das gesperrte
Ausgabefeld liest M18. Eingabe mit Enter bestätigen und in einer TIA-Beobachtungstabelle
kontrollieren. Ohne eigene Rechenlogik entsteht kein Rechenergebnis.
Die integrierte **HMI_Verbindung_2** über CPU-X1 und die daran gebundenen Tags sind entscheidend.
Die Diagnose-Meldungsanzeige überlagert teilweise das erste Feld; dies bleibt ein
Darstellungsrest des Technikprojekts und gehört nicht in das eigene Übungsbild.

Die Versuchsvorgabe **Basic DP / PROFIBUS** bleibt bestehen; das zusätzliche PN-HMI
ist die lokale Testalternative. DP-Runtimestart/Übersetzung sind geprüft, DP-Wertetransfer
nicht. PLCSIM Advanced emuliert keinen physischen PROFIBUS-DP-Bus, Verkabelung oder
Transfer in ein reales Panel. Spätere CPU-Starts endeten teils mit Timeout; zuverlässig
wiederholbare Starts bleiben offen. Keine Windows-Netzwerk-/Dienständerungen erforderlich.

### Tatsächlich geprüft – AT-1B/AT-4B

| Prüfung | Ergebnis |
|---|---|
| Native LabVIEW-Modell-/Dateifälle, 09.11.2026 | 28 + 10 bestanden |
| Drei Haupt-VIs nach Tastenfix neu gebaut, 13.11. | nativ ausführbar; 10 Frontpanel-Fälle und 5 Bedienabläufe erneut bestanden |
| Echte Mausbedienung | Alle Stationen geöffnet/gestartet; Fahrten beobachtet. Nach Fix ein Beenden-Klick an der Näherungsstation, danach unabhängig ausführbarer Leerlauf bestätigt. Fenster 1541×975 / maximiert 1914×1014. |
| Tastenfix | Aktionsschalter nur nach gelesener TRUE-Anforderung zurücksetzen; verhindert verlorene Klicks. Modell-/Unter-VIs unverändert. |
| PACTware offline | Parameterseiten, Speichern/Wiederöffnen geprüft; kein HART-/Messwerttransfer |
| TIA | Übungsstart nativ übersetzt/archiviert/dearchiviert. Technikarchiv frisch dearchiviert und mit CPU und beiden HMIs geöffnet; Softwareübersetzungen 0 Fehler/0 Warnungen, Hardware 0 Fehler/2 Schutzwarnungen. |
| Echte PN-HMI-Kopplung, 13.11. | HMI→SPS: 55 und 12,5; SPS→HMI: 7,75 und −2,25. Testbytes jeweils restauriert und zurückgelesen. Keine Rechen-FCs gelöst. |
| Offene Livepunkte | DP-Datenaustausch, negative HMI-Eingabe, wiederholbare CPU-Starts, alle geräteabhängigen DTM-Funktionen und reale Hardware. Keine zusätzlichen Liveprüfungen beim Repo-Upload. |

Vor Veröffentlichung wurden ausschließlich alte eigene Downloadprotokolle aus dem
Technikarchiv entfernt. Alle nativen Projektdateien blieben bytegleich; ZIP-CRC geprüft.
Die native Dearchivierung wurde vor dieser Protokollbereinigung nachgewiesen und danach
nicht erneut wiederholt. Alle eigenen Laufzeit-/MCP-/Computer-Use-Zugriffe sind beendet.
Die GitHub-Prüfung validiert Dateien und den C#-Teil; sie führt keine LabVIEW-/Siemens-Livetests aus.

Repo-Abnahme am 13.11.2026: `build.ps1 -Test -UiSmoke -SiemensCompile` bestand mit
129/129 Prüfgruppen, 803 Assertions und 143 WPF-Prüfungen; alle Builds ohne Fehler
oder Warnungen. `tools/check-public-content.ps1` prüfte die Veröffentlichungskandidaten
einschließlich VI-/PW6-Prüfsummen, relativer Projektreferenzen, leerer Messprotokolle
und lokaler Sprunglinks. Das portable Gesamtpaket enthält die Erweiterungen und
weiterhin nur die vier Dokumentationsdateien; ZIP-CRC und kopierte Prüfsummen bestanden.

<a name="de-modell"></a>

## Modell und Grenzen

Die Beispielgeometrie verwendet 1000 mm Bandlänge, Sensorpositionen bei
100/500/900 mm und einen 120-mm-Prüfling. Die Hallteilung beträgt im AT-2-Profil
1 mm und in AT-3/Referenz 1,2 mm je Impuls. Das sind konfigurierbare Beispielwerte,
keine Vermessung oder Laborkalibrierung der Hochschulanlage.

Unter **Anlagenparameter** werden unter anderem Maße, Sensoren, Hallteilung,
Materialeigenschaften, Motordynamik und Modellschritt eingestellt. Die digitale
Modellgeschwindigkeit wird im Profil festgelegt und ist auf 100 mm/s begrenzt.

Sensoren reagieren auf die geometrische Bedeckung; Hall-Flanken entstehen aus
Motorweg. 

Der Referenzcontroller bleibt für automatisierte Kerntests erhalten und ist kein auswählbarer Steuermodus.


<a name="de-pruefungen"></a>

## Tatsächlich ausgeführte Prüfungen

Die folgenden Ergebnisse vom **08.11.2026** gelten für die analoge Erweiterung;
die ältere Prüfhistorie steht anschließend unverändert darunter.

| Prüfung der analogen Erweiterung | Tatsächlich ausgeführtes Ergebnis |
|---|---|
| Releasebuild und Regression | `build.ps1 -Test -UiSmoke -SiemensCompile`: 128/128 Prüfgruppen, 779 Assertions; 135 WPF-Prüfungen bestanden. Adapter, LiveProbe und RestoreDefault: 0 Fehler, 0 Warnungen. |
| Automatisierte Analogtests ohne Siemens | Skalierung/Vorzeichen/Bytefolge, Adressgrenzen und Überlappung, Pflichtwörter, digitaler Rückweg, dynamischer Bedienkanal, unveränderte Geschwindigkeitsautorität der SPS, ungültige AQ-Werte und fehlende/vertauschte Prozessdaten geprüft. WPF-Profilformular inklusive Speichern/Laden der Analogadressen und Anfangsstellung geprüft. |
| Echte AI-Liveprüfung | In einer Verbindung las das FUP-Programm 0, 13824, 27648, −13824 und −27648 für 0, 5, 10, −5 und −10 V. Negative Werte nur im privaten Diagnosetest; Motor aus. |
| Echter MOVE / UNSCALE | MOVE: AQ 0/13824/27648 → Modellgeschwindigkeit 0/50/100 mm/s. Nativer UNSCALE: 35 % → AQ 9677 → 35,0007 mm/s rechts; 60 % → AQ 16589 → −60,0007 mm/s links. Modellmaximum 100 mm/s. |
| Gesamtanlage mit echtem SPS-Zähler | Bei 50 mm/s: 120-mm-Prüfling → 100 SPS-Impulse → 120 mm, Messung gültig. Testfaktor 1,2 mm/Impuls. Induktive/optische Sensoren, Lampen sowie Pause mit AQ = 0 ohne zusätzliche Bewegung oder Zählerimpulse bestanden. |
| Richtungskonflikt und ungültiger AQ | Beide Richtungsanforderungen bei AQ 16589: SPS schaltet beide Motorbefehle aus. AQ −13824: Produktionsadapter verwirft den Wert und beendet die Kopplung; keine Klemmung auf 0. |
| Vollständiger Bedienkanal mit echter SPS | Tatsächlicher WPF-Prozesshost → LiveProbe → V5-Adapter → SPS: leer verbinden, Prüfling einsetzen, Potentiometer 0→5→10→0 V während derselben Sitzung, AQ/Modellgeschwindigkeit 0→50→100→0, Motor ausschalten und Prüfling entfernen. Kontrolliertes Trennen ohne Prozessabbruch bestanden. |
| Grenzen und Cleanup | Sieben Kern-/Adapter-Livefälle und ein Live-Prozesskanaltest bestanden. Alle Prüfungen begrenzt; Test-CPU danach jeweils STOP / Default. Ein vorheriger 100-mm/s-Messlauf brach wegen zu großem tatsächlichen Synchronisationsabstand korrekt mit Aliasingfehler ab; der Schutz wurde beibehalten. |

Verwendet wurde ausschließlich die bestehende lokale Softbus-Test-CPU
`AT3_V2_Test_20261107` mit einer **privaten, getrennten Prüfkopie**, AI `IW14`
und AQ `QW4`. Diese Adressen gelten nur für deren nachgewiesene Hardwarekonfiguration.
Die Prüfkopie wurde mit nativen V18-Openness-Schnittstellen geladen; keine
Computer-Use-/UI-Automation. Nach der Test-IP-Anpassung gab die Hardwareübersetzung
zwei Zugriffsschutzwarnungen aus, keine Fehler; Download: 0 Fehler, 0 Warnungen.
Bei den Analogprüfungen vom 08.11.2026 blieben alle drei veröffentlichten
TIA-Pakete unverändert.

Die sichtbare WPF-Analogbedienung zusammen mit der SPS, WinCC und reale Hardware
wurden nicht live geprüft. WPF-Darstellung und Formularprüfung verwenden ausdrücklich
Siemensfreie Testdaten. Der reale Prozesskanaltest ist davon getrennt.

**Ergänzung 09.11.2026 – Bandmarkierung und Maßstab:** Releasebuild ohne Fehler
oder Warnungen; 129 Prüfgruppen mit 803 Assertions und 143 WPF-Prüfungen bestanden.
Geprüft wurden 10-mm-Teilstriche, gerenderte Markierungen bei 0/100 mm, drei
Modell-Kalibrierfahrten, Rückwärtsfahrt und Nullsetzen ohne Modell-/Hall-Reset.
Der echte UI-Button ist bei Bewegung oder historischen Daten gesperrt und setzt
bei frischem Stillstand ausschließlich die Markenreferenz. Diese Ergänzung wurde
ohne Siemens-Verbindung geprüft; kein neuer SPS-Livelauf.

**Adressprüfung 09.11.2026:** Das korrigierte AT-2-Startarchiv wurde mit TIA V18
archiviert, erneut dearchiviert und exportiert: „Länge in mm“ bleibt LReal auf
`%M10.0`. Die übrigen Variablen und das Bausteininventar sind unverändert.
Die Übersetzung meldet dieselben acht beabsichtigten Fehler wegen fehlender
Übungssignale wie zuvor, keine Warnungen. Kein Download in eine CPU.

| Prüfung | Ergebnis / Stand |
|---|---|
| Letzte Änderung: Messwertanzeige entfernt | Releasebuilds im Arbeitsprojekt und im vorbereiteten Repository: 0 Fehler, 0 Warnungen |
| WPF nach dieser Änderung | 128 Prüfungen bestanden, einschließlich Profilbedienung, Anzeige, stabiler Signalspalten und Tests mit eigenen Adapter-Testprozessen |
| Anlagenkern und Prozesskommunikation vor dieser Anzeigeänderung | 122 Testgruppen, 715 Assertions bestanden; anschließend nicht erneut ausgeführt, da unverändert |
| CPU-Rückstellung ohne Siemens-Zugriff | 26 Prüfungen bestanden; keine Siemens-Assembly geladen |
| Build gegen installiertes V5.0-SDK vor der Anzeigeänderung | Siemens-Adapter, LiveProbe und RestoreDefault ohne Fehler und Warnungen kompiliert |
| TIA-Projekterstellung | AT-3-Software ohne Fehler/Warnungen; AT-2 mit acht erwarteten offenen Signalen; beide Archivwiederherstellungen geprüft |
| Exportierte TIA-Projektstruktur | 56 Prüfungen der FUP-Graphen und 42 Datei-/Konfigurationsprüfungen bestanden |
| AT-3, zusätzliche Offlineprüfungen am 07.11.2026 | TIA-Übungsstart und separate Prüfvariante ohne Fehler und Warnungen übersetzt. 163 Prüfungen an nativen FUP-Exporten mit 45 modellierten Förderbandläufen bestanden; 15 Exportdateien nach Archivwiederherstellung inhaltlich identisch. |
| AT-3, zusätzliche Liveprüfungen am 07.11.2026 | Vier Prüfungen mit einer separaten Prüfvariante, PLCSIM Advanced V5.0 Update 2 und dem C#-Anlagenkern samt Kopplungscontroller und V5-Adapter bestanden: 120 mm → 100 SPS-Impulse → 120 mm; 60 mm → 50 SPS-Impulse → 60 mm. Verwendeter Beispiel-Testfaktor: 1,2 mm/Impuls. |
| Stopp/Fortsetzen und Hand/Automatik | Stopp bei Hall-HIGH ohne Zusatzimpuls; nach Fortsetzen weiterhin 100 Impulse und 120 mm. Wechsel auf Hand verwirft die Messung; Handfahrt erzeugt kein gültiges automatisches Ergebnis; Rückkehr auf Automatik ohne Wiederanlauf mitten auf der Strecke. |
| Build und Download für diese Liveprüfung | Anlagenkern, Kopplungscontroller, V5-Adapter und separater Prüfhelfer: 0 Fehler, 0 Warnungen. Hardware-/Softwaredownload in die Test-CPU: 0 Fehler, 0 Warnungen. Alle vier Läufe regulär beendet; Test-CPU anschließend STOP / Default. |

Die zusätzlichen Liveprüfungen verwendeten einen separaten Prüfhelfer zum Setzen
der Bedienmerker und zum Lesen der tatsächlichen SPS-Messwerte. WinCC-Bedienbilder
und die sichtbare WPF-Darstellung wurden dabei nicht geprüft.

Quellen: bereitgestellte AT-2-Anleitung WS24/25 mit sechsseitigem Anhang,
AT-3-Visualisierung WS24/25 rev3 und lokal installierte Siemens-Dokumentation.

**Hinweise zu projektinternen Fremdgrafiken stehen in den Drittanbieter-Lizenzen.**

<a name="de-bauen"></a>

## Bauen, testen und paketieren

Das .NET-SDK ist in `global.json` festgelegt, aktuell 10.0.402. Bei Bedarf lokal
installieren und anschließend aus dem Repositoryordner bauen:

```powershell
.\tools\setup-sdk.ps1
.\build.ps1 -Test -UiSmoke -SiemensCompile
.\start.ps1
```

NuGet benötigt beim ersten Build Internet. `-SiemensCompile` setzt das lokal
installierte V5.0-SDK voraus. Es wird im Windows-Verzeichnis für gemeinsame
32-Bit-Programmdateien unter `Siemens/PLCSIMADV/API/5.0` gesucht. Ein abweichender
Compilerpfad kann bei einem direkten Projektbuild mit
`-p:SiemensV5ApiPath=...` angegeben werden. Die proprietären Siemens-DLLs werden
nicht in das portable Paket kopiert.

Ohne Siemensinstallation lassen sich Anlagenkern, Oberfläche und Testprozesse
bauen und prüfen:

```powershell
.\build.ps1 -Test -UiSmoke
```

Die Oberfläche kann dann Profile bearbeiten und die Anlagenvorschau anzeigen.
Für den tatsächlichen Anlagenbetrieb fehlen ohne Adapterbuild und lokale
Siemensinstallation die Verbindungskomponenten.

Ein vollständiges portables Paket mit den eigenen Adapterprogrammen erstellen:

```powershell
.\build.ps1 -SiemensCompile
.\tools\package.ps1 -Destination output/AT2AT3-Portable -IncludeSiemensAdapter
```

Das erzeugt `output/AT2AT3-Portable.zip` und die zugehörige SHA-256-Datei. Für einen
weiteren Paketbau einen neuen Zielordner wählen. Persönliche Profile werden nicht
mitveröffentlicht; bei einem eigenen Umzug müssen sie zusätzlich mitgenommen werden.

Ohne `-IncludeSiemensAdapter` fehlen im Paket LiveProbe und RestoreDefault.
Ein solches Paket ermöglicht Vorschau und Profilbearbeitung, aber keine SPS-Verbindung.

Die vorbereiteten GitHub-/GitLab-Pipelines bauen und testen ohne Siemensinstallation.
Sie erzeugen entsprechend ein Paket ohne Siemens-Adapter. GitLab benötigt dafür
einen Windows-Runner und die Variable `WINDOWS_RUNNER_TAG`. Eine erfolgreiche
Ausführung einer Gitlab-Pipeline bleibt somit aus.

<div align="center">

[![README](https://img.shields.io/badge/README-24292f?style=for-the-badge)](README.md#deutsch)
[![Lizenz](https://img.shields.io/badge/Lizenz-24292f?style=for-the-badge)](LICENSE)
[![Drittanbieter](https://img.shields.io/badge/Drittanbieter-24292f?style=for-the-badge)](THIRD_PARTY_LICENSES.md)
[![⬆ Nach oben](https://img.shields.io/badge/%E2%AC%86_Nach_oben-24292f?style=for-the-badge)](#top)

</div>

---

<a name="english"></a>

# Guide: exercise projects and virtual Siemens PLC

Last updated: 13 November 2026.

<div align="center">

[![Start](https://img.shields.io/badge/Start-24292f?style=for-the-badge)](#en-start)
[![Profiles](https://img.shields.io/badge/Profiles-24292f?style=for-the-badge)](#en-profile)
[![Analogue](https://img.shields.io/badge/Analogue-24292f?style=for-the-badge)](#en-analog)
[![Operation](https://img.shields.io/badge/Operation-24292f?style=for-the-badge)](#en-betrieb)
[![TIA projects](https://img.shields.io/badge/TIA_projects-24292f?style=for-the-badge)](#en-tia-projekte)
[![AT-1B / AT-4B](https://img.shields.io/badge/AT--1B_%2F_AT--4B-24292f?style=for-the-badge)](#en-erweiterungen)
[![Model](https://img.shields.io/badge/Model-24292f?style=for-the-badge)](#en-modell)
[![Checks](https://img.shields.io/badge/Checks-24292f?style=for-the-badge)](#en-pruefungen)
[![Build](https://img.shields.io/badge/Build-24292f?style=for-the-badge)](#en-bauen)

</div>

<a name="en-start"></a>

## Starting the application and files

Windows x64 and .NET Framework 4.8 are required. Operating the simulated plant
also requires TIA Portal V18 and S7-PLCSIM Advanced V5.0 Update 2. The connection
works exclusively with an existing local virtual CPU through PLCSIM Softbus.

Extract the complete simulator package to a writable location and launch
`AT2AT3.Simulator.exe`. In a built source checkout, `Start.cmd` and `start.ps1`
also work. The application starts disconnected with an empty preview.
Selecting or saving a profile does not start the Siemens API.

| Folder | Contents |
|---|---|
| `config/profiles/` | Bundled profile templates |
| `profiles/` next to the EXE | Your saved profiles |
| `tia/` | Exercise archives and a copy of the reference project |
| `artifacts/` | Session reports; in the project directory for a source checkout, next to the application in the portable package |
| `live-probe/`, `restore-default/` | Project helper programs in packages that include the Siemens adapter |

To move the application, copy the entire application folder, including your
profiles. The Siemens components must be installed separately on the target
computer; set that computer's CPU instance name in the profile.

<a name="en-profile"></a>

## Interface and profiles

The top bar contains **Projektprofil** (project profile), **Mit SPS verbinden**
(connect to PLC), **SPS-Verbindung trennen** (disconnect from PLC) and
**CPU auf Default zurückstellen** (restore CPU to Default). Workpiece and
connection controls are on the left. The plant remains visible when switching
between **Bewegung** (motion) and **Signale** (signals).
**Verbindungsdetails und Ereignisse** (connection details and events) reveals
two additional tabs. **Weitere Aktionen** (more actions) contains recording
export and snapshot options.

Control comes exclusively from the virtual PLC.

---

<a name="en-profil-einrichten"></a>

### Configuring a profile

1. Select a template and click **Profil einrichten**. Bundled templates are
   copied to a profile of your own; your own profiles can be edited.
2. Enter the profile name and the exact CPU instance name from PLCSIM Advanced.
   The connection type is fixed to a local virtual CPU.
3. Assign the nine digital signals. All use BOOL. TIA variable names are freely
   selectable; the assigned addresses determine the mapping.
4. Enter addresses such as `I0.0` or `Q0.1`. An empty field means the assignment
   is still open. Under **E/A-Bereiche und digitale Geschwindigkeit** (I/O ranges
   and digital speed), enter the input and output range sizes actually available in TIA.
5. Select **Profil speichern** (save profile). Incomplete profiles can be saved;
   connecting requires a complete, valid mapping and the connection confirmations.

| Signal | Direction | AT-3 / reference default |
|---|---|---|
| Left sensor | C# → PLC input | I0.0 |
| Optical sensor | C# → PLC input | I0.1 |
| Right sensor | C# → PLC input | I0.2 |
| Hall signal | C# → PLC input | I0.3 |
| Green light | PLC output → C# | Q0.0 |
| Motor right | PLC output → C# | Q0.1 |
| Motor left | PLC output → C# | Q0.2 |
| Yellow light | PLC output → C# | Q0.3 |
| Red light | PLC output → C# | Q0.4 |

These addresses belong to the bundled project versions and can be changed.
They are initially left open in the AT-2 profile. Missing addresses are not
filled in automatically. Incorrect directions, invalid bit positions,
out-of-range addresses and overlaps are rejected. Other form entries are
preserved when an error occurs.

Use **Profile…** to copy, import and export profiles. Importing creates your own
copy. Switching profiles resets the plant preview and displayed recording;
it is only possible after disconnecting completely and completing any CPU
restoration in progress.

<a name="en-analog"></a>

### Analogue AT-2 coupling: potentiometer and motor speed

The **AT-2 Übung** profile enables **Analoge SPS-Drehzahl verwenden** by default.
In **Profil einrichten**, assign **Potentiometer Channel 5 · IW** and
**Drehzahlvorgabe Motor · QW** in addition to the nine digital signals. All AT-2
addresses are initially blank. TIA variable names are freely selectable; the
addresses, INT data type and module configuration determine the connection.

1. In your TIA working copy, configure an eight-channel voltage AI module for
   **−10…+10 V** and a four-channel voltage AQ module for **0…10 V**. Assign the
   used I/O areas to OB1's automatic process image. Coupling retains `SingleStep_C`.
2. Copy the potentiometer channel address and motor AQ address from TIA as
   `IW…` / `QW…`. “Channel 5” does not specify an absolute address. For channel
   number 5 and AI start byte B, it would be B + 2×5; use the actual channel
   address displayed in TIA.
3. Under **E/A-Bereiche und digitale Geschwindigkeit**, enter the total available
   area sizes in bytes. Each complete word must fit and must not overlap another
   signal. Incomplete mappings can be saved but cannot establish analogue coupling.
4. Under **Analoge Modulbereiche und Anfangsstellung**, set AI **−10 / +10 V**,
   AQ **0 / 10 V**, and the initial potentiometer voltage, normally **0 V**.
   Under **Anlagenparameter**, configure **Geschwindigkeit bei 10 V** and optional
   acceleration/deceleration. These are model parameters, not lab measurements.
5. Download your TIA program, set the CPU to RUN / Default, save the profile and
   connect. Insert the workpiece on the left once the belt is stationary and
   both motor commands are off.

**Potentiometer – SPS-Board, Channel 5** remains adjustable while connected. Its
AI word is published at the next input exchange; it does not directly change
motor speed. The requested raw value, last published AI word, actual AQ word
read from the PLC, output voltage and model speed are displayed separately.
Without a fresh connection, analogue PLC values are shown as unavailable.

The separate potentiometer on the **motor control module** is displayed as
fixed at **0 Ω / left stop**. No additional electrical effect is assumed.

| Voltage | AI at ±10 V | AQ at 0…10 V |
|---|---:|---:|
| −10 V | −27648 | invalid |
| −5 V | −13824 | invalid |
| 0 V | 0 | 0 |
| 5 V | 13824 | 13824 |
| 10 V | 27648 | 27648 |

The exchange uses signed 16-bit words in Siemens byte order. Normal potentiometer
operation covers 0…10 V; negative AI values are available only through the separate
diagnostic API. Other documented symmetric AI ranges can be selected in the profile;
the slider maximum follows the selected range. Use ±10 V for AT-2. Motor AQ remains
restricted to 0…10 V; invalid raw words abort the session rather than substituting speed.

**Check MOVE:** In your FBD solution, MOVE the AI raw value to the mapped AQ word.
Motor permissions and direction interlocking remain PLC tasks. With a model
maximum of 100 mm/s, 0, 5 and 10 V produce 0, 50 and 100 mm/s after adoption,
provided a direction is enabled. Allow for acceleration/deceleration if enabled.
Also compare AI and AQ in the TIA watch table. Turn off both PLC motor commands
before removing the workpiece; AQ = 0 alone does not unlock workpiece handling.

**Check UNSCALE:** In your FBD solution, convert 35.0 and 60.0 percent to the AQ
word with `LO_LIM = 0.0`, `HI_LIM = 100.0` and `BIPOLAR = FALSE`. Check `RET_VAL`
in TIA. Only the selected MOVE or UNSCALE path may write the AQ word. Expected
values are approximately 9677 and 16589: 3.5 and 6 V, or about 35 and 60 mm/s
at a 100 mm/s model maximum. Digital outputs still determine direction.
Counters, calibration and measured length remain in TIA.

To retain the previous digital test, disable analogue operation in a profile copy
and leave both analogue addresses blank. This mode still uses nine BOOL signals
and a fixed digital model speed. The analogue profile uses the actual PLC AQ
command exclusively to determine speed.

MOVE, UNSCALE, motor control, measurement and lights are not supplied as completed
student solutions in the AT-2 starter archive. The simulator exchanges
the two required analogue signals, not every unused module channel, wiring fault,
electrical characteristic or real calibration deviation.

<a name="en-betrieb"></a>

## Operating the plant and checking measurements

1. Complete the program download in TIA; do not interrupt a download in progress.
2. The intended virtual CPU must be in RUN and in PLCSIM Default mode.
3. Select the matching simulator profile. The three confirmations cover Siemens
   access, CPU synchronization, and the downloaded program with no TIA download in progress.
4. Select **Mit SPS verbinden**. The connection starts with an empty belt.
5. Once standstill is confirmed, select **Prüfling einsetzen (links)**. The PLC
   reacts to the sensor signals according to its program and controls the motor and lights.
6. When the belt is stationary, the workpiece can be removed at any position
   and inserted on the left again. The connection remains active after a pass.
7. To finish, select **SPS-Verbindung trennen**.

A PLC motor command that is already active is not overwritten when connecting.
Insertion and removal remain disabled while a motor command is active or the
belt is still slowing down. Operating permissions and stored sequence states
are controlled by the PLC program, TIA or your own HMI. The simulator does not
write controller memory bits.

### Belt marking and calibration run

The light belt marking and its pointer follow actual belt travel. The ruler has
ticks every **10 mm**; marker position is additionally displayed to **0.1 mm**.
While connected, with standstill confirmed and both motor commands off,
**Bandmarkierung auf 0** sets only the visual marker reference. Belt travel,
Hall phase and PLC counters are preserved.

For the calibration exercise, zero the marking, reset the PLC pulse counter
through TIA and use your PLC logic to move the belt approximately 100 mm right.
Stop and record the actual displayed marker position and PLC pulse count.
Repeat three times and average the three distance/pulse ratios. If the marking
leaves the visible scale, this is indicated; it does not jump back to the start.

Calibration, the measurement sequence and manual/automatic operation are
programmed in the PLC. **Check the measured value, Hall counter, calibration
factor and measurement status in the TIA watch table.**

---

<a name="en-freeze-und-laden"></a>

### Disconnecting, Freeze and downloading again

The connection synchronizes the virtual CPU using `SingleStep_C`. During this
process, the CPU may enter Freeze for data exchange. After a session it owns
ends normally, the simulator releases synchronization and checks the return
to Default mode.

Before downloading again from TIA, disconnect from the PLC and wait for
confirmation of Default mode. The simulator application may remain open.
The colored CPU indicator shows the last confirmed state; it does not
independently monitor ongoing changes made in TIA.

If Freeze persists, use **CPU auf Default zurückstellen** for the selected CPU.

**Erweiterte Testarten** (advanced test types) also offers a stationary I/O test
and a bounded conveyor acceptance test. For normal operation, keep
**Interaktive Verbindung** (interactive connection) selected.

<a name="en-tia-projekte"></a>

## TIA V18 exercise files

The starter archives are original reconstructions based on the supplied
documents, not official university templates. They were created as native
projects and retrieved using TIA.

<a name="en-at2"></a>

### AT-2

Retrieve `tia/AT2_Start_Uebung_Originalstand.zap18` into a new folder using
**Project → Retrieve**. A CPU 1516-3 PN/DP V2.8 is present; I/O modules are
intentionally absent. OB1, FC1/UNSCALE, FC2, an empty FC3, FC6/Testmessung and
the counter DB form the reconstructed starting state from the six-page appendix.

Motor interlocking, signal mapping, calibration, the measurement sequence and
signal lights remain exercise tasks. **Eight compilation errors caused by
missing global signals are intentional:** two in FC2 and six in FC6. The complete
project is not yet runnable. The simulator profile therefore has no I/O addresses initially.

The overlapping DInt addresses MD3/MD5 in the template were corrected to
MD24/MD20; the length is stored as LReal at M10.0. Boolean memory bits remain
M0.1, M2.5 and M4.0 through M4.3. Truncated names were completed.

**Address correction dated 9 November 2026:** The starter archive now assigns `%M10.0` with data type LReal to “Länge in mm”, matching the lab instructions. The former invalid `%ML32` entry has been replaced. The variable occupies memory bytes 10 through 17.

<a name="en-at3"></a>

### AT-3

Retrieve `tia/AT3_Start_Uebung_Originalstand.zap18`. CPU V2.8, DI32, DQ32,
AI8 (±10 V) and AQ4 (0–10 V) are present. Motor control and Hall measurement
are prepared using the personal reference project. The left sensor starts
the latched cycle; the right sensor ends it. Hall pulses within the optical
measurement window determine the length.

**OB1 network 5 is left empty for the signal lights.** The light variables are
prepared; the light logic is intentionally absent. Digital addresses are
listed in the profile configuration table.

The HMI has not yet been created. Add a KTP1200 Basic PN, `6AV2 123-2MB03-0AX0`,
device version 14.0.1.0, following the lab instructions. HMI screens, animations,
stop/resume and manual/automatic operation remain exercise tasks.

The optimized DB3 contains `HallImpulse` (Int), `StreckeMm` (LReal) and
`MillimeterProImpuls` (LReal, **test factor 1.2**). These variables can be used
symbolically in the TIA watch table; fixed DB offsets are not needed.
The test factor does not replace the calibration exercise.

<a name="en-referenz"></a>

### Reference project and a fresh starting state

`tia/AT2_AT3_Referenzprojekt.zip` contains a complete copy of the personal
reference project folder. Extract it completely, open `AT2_AT3_test/AT2_AT3.ap18`
and save a working copy. The original reference was not modified.
Checksums for the three packages are stored in `tia/manifest.json`.

For a fresh exercise, retrieve the unchanged starter archive again.
The V2.8 exercise CPUs differ from the V3.0 reference CPU. Hardware compilation
reported no errors, but two warnings about the missing access password and
unprotected CPU display.

<a name="en-erweiterungen"></a>

## AT-1B and AT-4B extensions

These are independent preparation tools; they do not require the C# conveyor simulator.
Original course documents and completed exercise solutions are not included.

### AT-1B: LabVIEW stations

1. Keep the complete repository/package together and open **`Start_AT1B.cmd`**,
   or open `extensions/AT1B/Pruefstand/AT1B_Pruefstaende.lvproj` in LabVIEW.
2. Choose `AT1B_Ultraschall.vi`, `AT1B_Radar.vi` or `AT1B_Naeherung.vi` and run
   with **Ctrl+R** or the Run arrow. Motion and recording only start when requested.
3. Set **Handposition**, then **Position setzen**; or select start/end, speed,
   dwell, repetitions and motion mode (shuttle/steps/one-way), then **Ablauf starten**.
   Pause holds motion; Stop ends it. Ultrasonic/proximity units are mm, radar uses m.
4. Enable **Messung eingeschaltet** and **Aufnahme eingeschaltet** to record.
   Choose a new CSV path and **CSV speichern**. Existing files are never overwritten.
   Eight blank sheets are in `extensions/AT1B/Messprotokolle/`.
5. Exit normally using **Pruefstand beenden**. Save unsaved data or explicitly allow
   discarding it. Scroll down for recording, CSV, exit and further device parameters.

Tested with LabVIEW 2026 Q3, 32-bit, 26.3.1f1. Normal use requires no MCP, Python,
DAQ device or Siemens software. All nine VI references are relative; native VIs
are editable in LabVIEW and XML/formula sources are included. Other LabVIEW versions
were not tested. `extensions/manifest.json` records hashes and verification status.

- **Ultrasonic:** starts without taught points, initially `NaN`. Turn
  **Teach-Taste gedrueckt** on, hold, then off. AU004 manual: 2–6 s to enter programming,
  then approximately 1 s each for P1/P2; over 6 s inverts. Reset: enter programming,
  disable echo, briefly confirm. The example instrument scale stays fixed after teaching.
- **Proximity:** select NJ8/CJ8 and specimen. **Axial s** moves s; **Seitlich x** moves x.
  **Andere Achse** sets the remaining coordinate. Zeroing changes only the ruler reference.
  Specimen width 24/40 mm is independent; calculate hysteresis, material factors and x→z yourself.
- **Radar:** fill level and sensor height determine physical distance. Apply sensor
  parameters with **Sensorabgleich uebernehmen**, then the controller separately with
  **Auswertung uebernehmen**. Echo/power loss invalidates measurements and switches the relay OFF.

**PACTware:** open `extensions/AT1B/PACTware/AT1B_Radar_Offline.PW6` in installed
PACTware 6.2 with VEGA-DTM 2.6.2.2; save your own copy. Hierarchy: VEGA USB →
VEGAMET 391 → VEGAPULS C 20 series HART (the C22 device family). Double-click a
device for offline parameters. The catalogue only lists devices compatible with the
selected parent. Application, units, adjustment, damping, linearization and trend pages
were opened; saving/reopening a private parameter copy was verified. Without a device,
the trend has no measurements. There is no LabVIEW-DTM/HART connection.
The tested default VEGAMET adjustment uses mA. Digital HART evaluation requires an
RS232/Ethernet device option according to the manufacturer. Distance adjustment and
relay 2 were not verified in this offline DTM (relays 1 and 6 were visible).
Do not substitute unrelated relays or units for the exercise requirements.

### AT-1B model assumptions

| Area | Basis and limitation |
|---|---|
| AU004 | Datasheet: 50–400 mm, 4–20 mA, 3 mm resolution, 0.3 s startup. Fixed example display scale 50–400 mm; analogue ramp from blind zone to P1. Not the original university VI. |
| Teach/faults | Brief-press window 0.7–1.5 s, 30 s timeout and 3 mm hysteresis are examples. Reset returns to unconfigured; invalid/missing measurements use NaN, not invented fault currents. |
| NJ8 | Example: 8 mm nominal distance, 7.5% hysteresis; datasheet factors Cu 0.40 / Al 0.42 / brass 0.50 / V2A 0.72. Not a calibration of an individual sensor. |
| CJ8/lateral curve | Adjustable example material factors/sensitivity; elliptical lateral edge, example reach 12 mm. NJ8 magnet factor 1.05 is also an assumption. Width 24/40 mm and both axes act separately. |
| Electrical model | Ideal 0/24 V PNP output without load, cable or leakage effects. |
| Radar | Example sensor height 1.5 m; 60 L alone does not establish tank shape. First-order damping, linear or horizontal-cylinder geometry as alternatives; independent current/controller chain and relay window. |
| Time/recording | Fixed 20 ms model steps; Windows may slow the sequence. Record every 100 ms model time, up to 10,000 rows. No real-time or hardware-equivalence guarantee. |

Sources: AT-1B exercise manual pp. 9–22, AU004 datasheet/manual,
NJ8-18GM50-E2 (282965), CJ8-18GM-E2 (019462). Configuration and analysis remain
student tasks. [VEGAMET 391 manual: analogue/digital inputs and device options](https://www.vega.com/api/sitecore/DocumentDownload/Handler?documentContainerId=4478&documentGroupId=36032&fileExtension=pdf&languageId=1&version=03-06-2025).

### AT-4B: starter and separate technical test

Retrieve `tia/AT4B_Start_Uebung_Nachbildung_3AN02.zap18` using **Project → Retrieve**
in TIA V18 into a new short directory. It contains a CPU and empty FBD Main/OB1 with
simulation enabled. Create HMI, tags, FCs, arithmetic and OB1 calls yourself.
The addresses shown on exercise p. 9 are in `extensions/AT4B/Quellenreferenz_Adressen.csv`;
they are not pre-imported. LReal `%M2.0`, `%M10.0`, `%M18.0` each occupies eight bytes;
`%M26.0` is separate. No unsupported function was invented for the illustrated name `Q1`.

The exercise footnote names 3AP02, which was unavailable in the installed catalogue.
The supplied datasheet names 3AN02. This reconstruction therefore uses
**6ES7516-3AN02-0AB0, V2.8**; the firmware is a local catalogue choice.

`tia/AT4B_Technikpruefung_20261113.zap18` is a separate technical reference without
an arithmetic solution. Retrieve it, explicitly download **PLC_1** to a local virtual
S7-1500 in PLCSIM Softbus and put it into **RUN / Default**. The verified setup used
`AT4B_Verbindungstest_20261110`, CPU-X1 `192.168.0.78`.
Select **HMI_2 [KTP1200 Basic PN] → Online → Simulation → Start**, without an additional
tag simulator. The two input fields write LReal to M2/M10; the read-only output reads M18.
Confirm entries with Enter and check a TIA watch table. Without your arithmetic logic,
the inputs do not produce a calculated result. The integrated **HMI_Verbindung_2**
through CPU-X1 and the tag bindings to that connection are essential.
The diagnostic alarm view partly overlaps the first input field; this remains a display
issue in the technical reference and does not belong in your own exercise screen.

The original requirement remains **Basic DP / PROFIBUS**; the added PN HMI is the
local test alternative. DP runtime startup/compilation were checked; DP value transfer
was not. PLCSIM Advanced does not emulate a physical PROFIBUS-DP bus, wiring or real
panel transfer. Later CPU starts sometimes timed out; reliably repeatable starts remain
open. No Windows network or service changes are required.

### Checks actually performed – AT-1B/AT-4B

| Check | Result |
|---|---|
| Native LabVIEW model/file cases, 9 November 2026 | 28 + 10 passed |
| Three main VIs rebuilt after button fix, 13 November | Native executable; 10 front-panel cases and 5 workflows passed again |
| Actual mouse operation | All stations opened/started, motion observed. One proximity Quit click after the fix, independently confirmed executable idle. Windows 1541×975 / maximized 1914×1014. |
| Button fix | Reset action buttons only after a sampled TRUE request, preventing lost clicks. Model/subVIs unchanged. |
| PACTware offline | Parameter pages and save/reopen checked; no HART/measurement transfer |
| TIA | Starter natively compiled/archived/retrieved. Technical archive freshly retrieved and opened with CPU and both HMIs; software compilation 0 errors/0 warnings, hardware 0 errors/2 protection warnings. |
| Actual PN-HMI coupling, 13 November | HMI→PLC: 55 and 12.5; PLC→HMI: 7.75 and −2.25. Original test bytes restored and read back each time. No arithmetic FC solution provided. |
| Open live checks | DP transfer, negative HMI input, repeatable CPU startup, all device-specific DTM functions and physical hardware. No additional live checks during repository publication. |

Only old assistant-generated download logs were removed from the technical archive
before publication. All native project members remain byte-identical; ZIP CRC checked.
Native retrieval was verified before log removal and was not repeated afterwards.
All owned runtime/MCP/Computer Use sessions were ended. GitHub checks files and the
C# component; it does not execute LabVIEW/Siemens live tests.

Repository checks on 13 November 2026: `build.ps1 -Test -UiSmoke -SiemensCompile`
passed 129/129 test groups, 803 assertions and 143 WPF checks; all builds had no
errors or warnings. `tools/check-public-content.ps1` checked publication candidates,
including VI/PW6 hashes, relative project references, blank sheets and local anchors.
The portable package includes the extensions and only the same four documentation
files; ZIP CRC and copied file hashes passed.

<a name="en-modell"></a>

## Model and limitations

The example geometry uses a 1000 mm belt, sensor positions at 100/500/900 mm
and a 120 mm workpiece. Hall pitch is 1 mm per pulse in the AT-2 profile and
1.2 mm per pulse in AT-3/reference. These are configurable example values,
not measurements or laboratory calibration of the university equipment.

**Anlagenparameter** (plant parameters) includes dimensions, sensors, Hall pitch,
material properties, motor dynamics and the model time step. The digital model
speed is set in the profile and limited to 100 mm/s.

Sensors react to geometric coverage; Hall edges are generated from motor travel.

The reference controller is retained for automated core tests and is not a
selectable control mode.

<a name="en-pruefungen"></a>

## Checks actually performed

The following results dated **8 November 2026** apply to the analogue extension;
the earlier verification history remains below.

| Analogue extension check | Result actually obtained |
|---|---|
| Release build and regression | `build.ps1 -Test -UiSmoke -SiemensCompile`: 128/128 test groups, 779 assertions; 135 WPF checks passed. Adapter, LiveProbe and RestoreDefault: 0 errors, 0 warnings. |
| Automated analogue tests without Siemens | Scaling/sign/byte order, address limits and overlap, required words, digital compatibility, live control channel, unchanged PLC speed authority, invalid AQ and missing/mismatched process data checked. WPF profile form checked, including saved/reloaded analogue addresses and initial voltage. |
| Actual AI live test | Within one connection, the FBD program read 0, 13824, 27648, −13824 and −27648 for 0, 5, 10, −5 and −10 V. Negative values were limited to the private diagnostic test; motor off. |
| Actual MOVE / UNSCALE | MOVE: AQ 0/13824/27648 → model speed 0/50/100 mm/s. Native UNSCALE: 35 % → AQ 9677 → 35.0007 mm/s right; 60 % → AQ 16589 → −60.0007 mm/s left. Model maximum 100 mm/s. |
| Complete plant with actual PLC counter | At 50 mm/s: 120-mm workpiece → 100 PLC pulses → 120 mm, measurement valid. Test factor 1.2 mm/pulse. Inductive/optical sensors, lights and an AQ = 0 pause without added motion or counter pulses passed. |
| Direction conflict and invalid AQ | Both direction requests with AQ 16589: PLC turns both motor commands off. AQ −13824: production adapter rejects the value and ends coupling; no clamping to zero. |
| Complete control channel with actual PLC | Actual WPF process host → LiveProbe → V5 adapter → PLC: connect empty, insert workpiece, change potentiometer 0→5→10→0 V within the same session, AQ/model speed 0→50→100→0, turn motor off and remove workpiece. Controlled disconnection passed without killing the process. |
| Limits and cleanup | Seven core/adapter live cases and one live process-channel case passed. Every test was bounded; test CPU subsequently STOP / Default. An earlier 100-mm/s measurement run correctly aborted on an excessive actual synchronization gap with an aliasing error; the protection was retained. |

Only the existing local Softbus test CPU `AT3_V2_Test_20261107` was used, with a
**private, separate test copy**, AI `IW14` and AQ `QW4`. These addresses apply
only to its verified hardware configuration. The copy was downloaded through
native V18 Openness interfaces; no Computer Use/UI Automation. After changing
the test project's IP, hardware compilation reported two access-protection
warnings and no errors; download: 0 errors, 0 warnings. During the analogue checks
on 8 November 2026, all three published TIA packages remained unchanged.

Visible WPF analogue interaction together with the PLC, WinCC and physical
hardware were not tested live. WPF rendering and form checks explicitly use
Siemens-free test data. The actual process-channel test is separate.

**Update 9 November 2026 – belt marking and ruler:** Release build without errors
or warnings; 129 test groups with 803 assertions and 143 WPF checks passed.
Checks covered 10-mm ticks, rendered markings at 0/100 mm, three model calibration
runs, reverse motion and zeroing without resetting the model or Hall phase.
The actual UI button is disabled during motion or with historical data; with fresh
standstill it changes only the marker reference. This update was checked without
a Siemens connection; no additional PLC live run was performed.

**Address verification, 9 November 2026:** The corrected AT-2 starter archive was
archived, retrieved again and exported using TIA V18: “Länge in mm” remains LReal
at `%M10.0`. All other variables and the block inventory are unchanged.
Compilation reports the same eight intentional errors for missing exercise
signals as before, with no warnings. No download to a CPU was performed.

| Check | Result / state |
|---|---|
| Latest change: measurement display removed | Release builds in the development project and prepared repository: 0 errors, 0 warnings |
| WPF after this change | 128 checks passed, including profile operation, display, stable signal columns and tests using project adapter test processes |
| Plant core and process communication before this display change | 122 test groups, 715 assertions passed; not rerun afterwards because they were unchanged |
| CPU restoration without Siemens access | 26 checks passed; no Siemens assembly loaded |
| Build against the installed V5.0 SDK before the display change | Siemens adapter, LiveProbe and RestoreDefault compiled without errors or warnings |
| TIA project creation | AT-3 software without errors/warnings; AT-2 with eight expected unresolved signals; retrieval of both archives checked |
| Exported TIA project structure | 56 FBD graph checks and 42 file/configuration checks passed |
| AT-3, additional offline checks on 2026-11-07 | TIA starter and separate test variant compiled with no errors or warnings. 163 checks of native FBD exports, including 45 modeled conveyor runs, passed; 15 exported files were semantically identical after archive retrieval. |
| AT-3, additional live checks on 2026-11-07 | Four checks with a separate test variant, PLCSIM Advanced V5.0 Update 2 and the C# plant core, coupling controller and V5 adapter passed: 120 mm → 100 PLC pulses → 120 mm; 60 mm → 50 PLC pulses → 60 mm. Example test factor: 1.2 mm/pulse. |
| Stop/resume and manual/automatic | Stopping while Hall was HIGH added no pulse; resuming still resulted in 100 pulses and 120 mm. Switching to manual invalidated the measurement; manual jogging produced no valid automatic result; returning to automatic caused no restart mid-route. |
| Build and download for this live verification | Plant core, coupling controller, V5 adapter and separate diagnostic helper: 0 errors, 0 warnings. Hardware/software download to the test CPU: 0 errors, 0 warnings. All four runs exited normally; the test CPU was subsequently in STOP / Default. |

The additional live checks used a separate diagnostic helper to set command
markers and read the actual PLC measurements. WinCC screens and the visible
WPF rendering were not tested.

Sources: supplied AT-2 instructions WS24/25 with the six-page appendix,
AT-3 visualization WS24/25 rev3 and locally installed Siemens documentation.

**Notes on third-party graphics within the projects are in the third-party licenses.**

<a name="en-bauen"></a>

## Building, testing and packaging

The .NET SDK version is pinned in `global.json`, currently 10.0.402. Install it
locally if necessary, then build from the repository directory:

```powershell
.\tools\setup-sdk.ps1
.\build.ps1 -Test -UiSmoke -SiemensCompile
.\start.ps1
```

NuGet requires internet access for the first build. `-SiemensCompile` requires
the locally installed V5.0 SDK. It is looked up under `Siemens/PLCSIMADV/API/5.0`
in the Windows Common Program Files (x86) directory. For a direct project build,
a different compiler reference path can be supplied with `-p:SiemensV5ApiPath=...`.
Proprietary Siemens DLLs are not copied into the portable package.

Without a Siemens installation, the plant core, interface and test processes
can be built and checked:

```powershell
.\build.ps1 -Test -UiSmoke
```

The interface can then edit profiles and show the plant preview. Actual plant
operation requires the connection components supplied by the adapter build
and a local Siemens installation.

To create a complete portable package including the project's adapter programs:

```powershell
.\build.ps1 -SiemensCompile
.\tools\package.ps1 -Destination output/AT2AT3-Portable -IncludeSiemensAdapter
```

This creates `output/AT2AT3-Portable.zip` and its SHA-256 file. Choose a new
destination directory for another package build. Personal profiles are not
published; include them separately when moving your own installation.

Without `-IncludeSiemensAdapter`, the package does not include LiveProbe or
RestoreDefault. Such a package supports preview and profile editing, but no PLC connection.

The prepared GitHub/GitLab pipelines build and test without a Siemens installation.
They therefore produce a package without the Siemens adapter. GitLab requires
a Windows runner and the `WINDOWS_RUNNER_TAG` variable. A successful GitLab
pipeline run is therefore still outstanding.

<div align="center">

[![README](https://img.shields.io/badge/README-24292f?style=for-the-badge)](README.md#english)
[![License](https://img.shields.io/badge/License-24292f?style=for-the-badge)](LICENSE)
[![Third-party licenses](https://img.shields.io/badge/Third--party_licenses-24292f?style=for-the-badge)](THIRD_PARTY_LICENSES.md)
[![⬆ Back to top](https://img.shields.io/badge/%E2%AC%86_Back_to_top-24292f?style=for-the-badge)](#top)

</div>
