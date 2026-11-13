<?xml version="1.0" encoding="UTF-8"?>
<Project Type="Project" LVVersion="26008000">
  <Property Name="NI.LV.All.SourceOnly" Type="Bool">true</Property>
  <Property Name="NI.Project.Description" Type="Str">Virtuelle AT-1B-Messwerkbank. Eigene Nachbildung, keine Hochschulvorlage und keine echte DAQ-/HART-Hardware.</Property>
  <Item Name="My Computer" Type="My Computer">
    <Property Name="server.app.propertiesEnabled" Type="Bool">true</Property>
    <Property Name="server.control.propertiesEnabled" Type="Bool">true</Property>
    <Property Name="server.vi.callsEnabled" Type="Bool">true</Property>
    <Property Name="server.vi.propertiesEnabled" Type="Bool">true</Property>
    <Property Name="server.tcp.enabled" Type="Bool">false</Property>
    <Item Name="AT1B_Ultraschall.vi" Type="VI" URL="AT1B_Ultraschall.vi"/>
    <Item Name="AT1B_Radar.vi" Type="VI" URL="AT1B_Radar.vi"/>
    <Item Name="AT1B_Naeherung.vi" Type="VI" URL="AT1B_Naeherung.vi"/>
    <Item Name="Modelle" Type="Folder">
      <Item Name="Trajektorie.vi" Type="VI" URL="Modelle/Trajektorie.vi"/>
      <Item Name="AU004_Schritt.vi" Type="VI" URL="Modelle/AU004_Schritt.vi"/>
      <Item Name="Radar_Schritt.vi" Type="VI" URL="Modelle/Radar_Schritt.vi"/>
      <Item Name="Naeherung_Schritt.vi" Type="VI" URL="Modelle/Naeherung_Schritt.vi"/>
    </Item>
    <Item Name="SubVIs" Type="Folder">
      <Item Name="CSV_Speichern.vi" Type="VI" URL="SubVIs/CSV_Speichern.vi"/>
      <Item Name="Aufzeichnung_Schritt.vi" Type="VI" URL="SubVIs/Aufzeichnung_Schritt.vi"/>
    </Item>
    <Item Name="Dependencies" Type="Dependencies"/>
    <Item Name="Build Specifications" Type="Build"/>
  </Item>
</Project>
