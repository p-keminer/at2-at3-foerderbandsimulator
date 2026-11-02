# Drittanbieter-Lizenzen


## Microsoft: dotnet-install.ps1

Das unveränderte `tools/dotnet-install.ps1` stammt von Microsoft / .NET Foundation
und Contributors. Bezugsquelle: [dot.net](https://dot.net/v1/dotnet-install.ps1).
Projekt und Lizenz: [dotnet/install-scripts](https://github.com/dotnet/install-scripts).

Der folgende ursprüngliche Lizenztext gilt für dieses Installationsskript:

```text
The MIT License (MIT)

Copyright (c) .NET Foundation and Contributors

All rights reserved.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Abgrenzung

Die [Projektlizenz](LICENSE) betrifft den eigenen Simulatorcode und die eigene
Dokumentation. Hochschulunterlagen, Kurs-Screenshots, Fotos, Schaltbilder,
originale Hochschul-TIA-Vorlagen, Siemens-Installationsdateien, SDK-DLLs und Lizenzdaten
werden nicht mitgeliefert. Unter `tia/` liegen eigene rekonstruierte Übungsprojekte
und eine vollständige Kopie des eigenen Referenzprojekts. Darin enthaltene
projektinterne Fremdgrafiken (insbesondere das Foto der Fahrbandstrecke) erhalten
durch die Projektlizenz keine neue Lizenz; die Rechte verbleiben beim jeweiligen
Rechteinhaber.

Das .NET-SDK und die .NET-Framework-Referenzpakete werden nur zum Bauen benötigt.
Sie liegen in ignorierten Ordnern und gehören nicht zum portablen Simulatorpaket. Die
vorhandene Windows-.NET-Framework-Laufzeit stellt WPF und Systemschriften.

Siemens, SIMATIC, TIA Portal, WinCC und S7-PLCSIM sind Bezeichnungen ihrer
jeweiligen Rechteinhaber. Dieses studentische Projekt ist weder eine offizielle
Hochschulvorlage noch ein von Siemens herausgegebenes Produkt.
