<a id="top"></a>

<div align="center">

[![🇩🇪 Deutsch](https://img.shields.io/badge/%F0%9F%87%A9%F0%9F%87%AA_Deutsch-24292f?style=for-the-badge)](#deutsch)
[![🇬🇧 English](https://img.shields.io/badge/%F0%9F%87%AC%F0%9F%87%A7_English-24292f?style=for-the-badge)](#english)

</div>

<a id="deutsch"></a>

# Anleitung: Übungsprojekte und virtuelle Siemens-SPS

Stand: 01.11.2026.

<div align="center">

[![Start](https://img.shields.io/badge/Start-24292f?style=for-the-badge)](#de-start)
[![Profile](https://img.shields.io/badge/Profile-24292f?style=for-the-badge)](#de-profile)
[![Betrieb](https://img.shields.io/badge/Betrieb-24292f?style=for-the-badge)](#de-betrieb)
[![TIA-Projekte](https://img.shields.io/badge/TIA--Projekte-24292f?style=for-the-badge)](#de-tia-projekte)
[![Modell](https://img.shields.io/badge/Modell-24292f?style=for-the-badge)](#de-modell)
[![Prüfungen](https://img.shields.io/badge/Pr%C3%BCfungen-24292f?style=for-the-badge)](#de-pruefungen)
[![Bauen](https://img.shields.io/badge/Bauen-24292f?style=for-the-badge)](#de-bauen)

</div>

<a id="de-start"></a>

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

<a id="de-profile"></a>

## Oberfläche und Profile

Oben stehen **Projektprofil**, **Mit SPS verbinden**, **SPS-Verbindung trennen**
und **CPU auf Default zurückstellen**. Links werden Prüfling und Verbindung
bedient. Die Anlage bleibt beim Wechsel zwischen **Bewegung** und **Signale**
sichtbar. **Verbindungsdetails und Ereignisse** blendet zwei zusätzliche Reiter ein.
**Weitere Aktionen** enthält Aufzeichnungsexport und Snapshot.

Die Steuerung kommt ausschließlich aus der virtuellen SPS.

---

<a id="de-profil-einrichten"></a>

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

<a id="de-betrieb"></a>

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

Kalibrierung, Messablauf und Hand-/Automatikbetrieb werden in der SPS programmiert.
**Messwert, Hallzähler, Kalibrierfaktor und Messstatus werden du in der
TIA-Beobachtungstabelle geprüft.**

---

<a id="de-freeze-und-laden"></a>

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

<a id="de-tia-projekte"></a>

## TIA-V18-Übungsdateien

Die Startarchive sind eigene Rekonstruktionen aus den bereitgestellten Unterlagen,
keine originalen Hochschulvorlagen. Sie wurden nativ erzeugt und mit TIA wiederhergestellt.

<a id="de-at2"></a>

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
korrigiert; die Länge als LReal liegt auf ML32. Bool-Merker bleiben M0.1, M2.5 und
M4.0 bis M4.3. Abgeschnittene Namen wurden vervollständigt.


<a id="de-at3"></a>

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

<a id="de-referenz"></a>

### Referenz und neuer Ausgangszustand

`tia/AT2_AT3_Referenzprojekt.zip` enthält eine vollständige Kopie des eigenen
Referenzprojektordners. Vollständig entpacken, `AT2_AT3_test/AT2_AT3.ap18` öffnen
und als Arbeitskopie speichern. Die ursprüngliche Referenz wurde nicht verändert.
Die Prüfsummen der drei Pakete stehen in `tia/manifest.json`.

Für einen neuen Übungsversuch das unveränderte Startarchiv erneut dearchivieren.
Die Übungs-CPUs V2.8 unterscheiden sich von der Referenz-CPU V3.0. Bei der
Hardwareübersetzung gab es keine Fehler, aber zwei Warnungen zu fehlendem
Zugriffspasswort und ungeschütztem CPU-Display.

<a id="de-modell"></a>

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


<a id="de-pruefungen"></a>

## Tatsächlich ausgeführte Prüfungen

| Prüfung | Ergebnis / Stand |
|---|---|
| Letzte Änderung: Messwertanzeige entfernt | Releasebuilds im Arbeitsprojekt und im vorbereiteten Repository: 0 Fehler, 0 Warnungen |
| WPF nach dieser Änderung | 128 Prüfungen bestanden, einschließlich Profilbedienung, Anzeige, stabiler Signalspalten und Tests mit eigenen Adapter-Testprozessen |
| Anlagenkern und Prozesskommunikation vor dieser Anzeigeänderung | 122 Testgruppen, 715 Assertions bestanden; anschließend nicht erneut ausgeführt, da unverändert |
| CPU-Rückstellung ohne Siemens-Zugriff | 26 Prüfungen bestanden; keine Siemens-Assembly geladen |
| Build gegen installiertes V5.0-SDK vor der Anzeigeänderung | Siemens-Adapter, LiveProbe und RestoreDefault ohne Fehler und Warnungen kompiliert |
| TIA-Projekterstellung | AT-3-Software ohne Fehler/Warnungen; AT-2 mit acht erwarteten offenen Signalen; beide Archivwiederherstellungen geprüft |
| Exportierte TIA-Projektstruktur | 56 Prüfungen der FUP-Graphen und 42 Datei-/Konfigurationsprüfungen bestanden |

Quellen: bereitgestellte AT-2-Anleitung WS24/25 mit sechsseitigem Anhang,
AT-3-Visualisierung WS24/25 rev3 und lokal installierte Siemens-Dokumentation.

**Hinweise zu projektinternen Fremdgrafiken stehen in den Drittanbieter-Lizenzen.**

<a id="de-bauen"></a>

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

<a id="english"></a>

# Guide: exercise projects and virtual Siemens PLC

Last updated: 1 November 2026.

<div align="center">

[![Start](https://img.shields.io/badge/Start-24292f?style=for-the-badge)](#en-start)
[![Profiles](https://img.shields.io/badge/Profiles-24292f?style=for-the-badge)](#en-profile)
[![Operation](https://img.shields.io/badge/Operation-24292f?style=for-the-badge)](#en-betrieb)
[![TIA projects](https://img.shields.io/badge/TIA_projects-24292f?style=for-the-badge)](#en-tia-projekte)
[![Model](https://img.shields.io/badge/Model-24292f?style=for-the-badge)](#en-modell)
[![Checks](https://img.shields.io/badge/Checks-24292f?style=for-the-badge)](#en-pruefungen)
[![Build](https://img.shields.io/badge/Build-24292f?style=for-the-badge)](#en-bauen)

</div>

<a id="en-start"></a>

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

<a id="en-profile"></a>

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

<a id="en-profil-einrichten"></a>

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

<a id="en-betrieb"></a>

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

Calibration, the measurement sequence and manual/automatic operation are
programmed in the PLC. **Check the measured value, Hall counter, calibration
factor and measurement status in the TIA watch table.**

---

<a id="en-freeze-und-laden"></a>

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

<a id="en-tia-projekte"></a>

## TIA V18 exercise files

The starter archives are original reconstructions based on the supplied
documents, not official university templates. They were created as native
projects and retrieved using TIA.

<a id="en-at2"></a>

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
MD24/MD20; the length is stored as LReal at ML32. Boolean memory bits remain
M0.1, M2.5 and M4.0 through M4.3. Truncated names were completed.

<a id="en-at3"></a>

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

<a id="en-referenz"></a>

### Reference project and a fresh starting state

`tia/AT2_AT3_Referenzprojekt.zip` contains a complete copy of the personal
reference project folder. Extract it completely, open `AT2_AT3_test/AT2_AT3.ap18`
and save a working copy. The original reference was not modified.
Checksums for the three packages are stored in `tia/manifest.json`.

For a fresh exercise, retrieve the unchanged starter archive again.
The V2.8 exercise CPUs differ from the V3.0 reference CPU. Hardware compilation
reported no errors, but two warnings about the missing access password and
unprotected CPU display.

<a id="en-modell"></a>

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

<a id="en-pruefungen"></a>

## Checks actually performed

| Check | Result / state |
|---|---|
| Latest change: measurement display removed | Release builds in the development project and prepared repository: 0 errors, 0 warnings |
| WPF after this change | 128 checks passed, including profile operation, display, stable signal columns and tests using project adapter test processes |
| Plant core and process communication before this display change | 122 test groups, 715 assertions passed; not rerun afterwards because they were unchanged |
| CPU restoration without Siemens access | 26 checks passed; no Siemens assembly loaded |
| Build against the installed V5.0 SDK before the display change | Siemens adapter, LiveProbe and RestoreDefault compiled without errors or warnings |
| TIA project creation | AT-3 software without errors/warnings; AT-2 with eight expected unresolved signals; retrieval of both archives checked |
| Exported TIA project structure | 56 FBD graph checks and 42 file/configuration checks passed |

Sources: supplied AT-2 instructions WS24/25 with the six-page appendix,
AT-3 visualization WS24/25 rev3 and locally installed Siemens documentation.

**Notes on third-party graphics within the projects are in the third-party licenses.**

<a id="en-bauen"></a>

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
