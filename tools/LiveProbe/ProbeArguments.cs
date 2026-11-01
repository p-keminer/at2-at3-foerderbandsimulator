using System;
using System.Collections.Generic;
using System.Linq;
using Plant.Core;

internal sealed class ProbeArguments
{
    private static readonly string[] Flags = { "--freigabe-runtime", "--freigabe-cpu-schritte", "--minimal-two-signal", "--confirm-tia-minimal", "--cycle-control-point", "--confirm-ob1-automatic",
        "--digital-plant", "--confirm-tia-digital", "--allow-movement", "--require-transport", "--stream" };
    private static readonly string[] Values = { "--map", "--partition", "--report", "--steps", "--sdk", "--profile", "--plant", "--max-virtual-seconds", "--wall-seconds" };
    private readonly Dictionary<string, string?> parsed = new();
    public bool Minimal => parsed.ContainsKey("--minimal-two-signal");
    public bool CycleControlPoint => parsed.ContainsKey("--cycle-control-point");
    public bool Digital => parsed.ContainsKey("--digital-plant");
    public bool Has(string key) => parsed.ContainsKey(key);
    public static bool SupportedSdkVersion(string version) => version == "5.0.0.0" || version == "5.0.0.2";
    public static bool SupportedCycleSdkVersion(string version, string fileVersion) => version == "5.0.0.2" && fileVersion == "50.43.04.00";
    public string Required(string key) => parsed.TryGetValue(key, out var value) && value != null ? value : throw new ArgumentException("Argument fehlt: " + key);
    public string Optional(string key, string fallback) => parsed.ContainsKey(key) ? Required(key) : fallback;
    public static ProbeArguments Parse(string[] args)
    {
        var result = new ProbeArguments();
        for (int i = 0; i < args.Length; i++)
        {
            string key = args[i];
            Numbers.Require(!result.parsed.ContainsKey(key), "Doppeltes Argument: " + key);
            if (Flags.Contains(key)) result.parsed.Add(key, null);
            else if (Values.Contains(key))
            {
                Numbers.Require(i + 1 < args.Length && !args[i + 1].StartsWith("--"), "Wert fehlt: " + key);
                result.parsed.Add(key, args[++i]);
            }
            else throw new ArgumentException("Unbekanntes Argument: " + key);
        }
        Numbers.Require(result.parsed.ContainsKey("--freigabe-runtime") && result.parsed.ContainsKey("--freigabe-cpu-schritte"), "Beide gesonderten Livefreigaben erforderlich.");
        Numbers.Require(!result.Minimal || result.parsed.ContainsKey("--confirm-tia-minimal"), "Minimaltest: --confirm-tia-minimal erforderlich.");
        Numbers.Require(!result.Minimal || !result.parsed.ContainsKey("--steps"), "Minimaltest hat fest drei Fortschaltungen; --steps ist nicht erlaubt.");
        Numbers.Require(result.Minimal || !result.parsed.ContainsKey("--confirm-tia-minimal"), "TIA-Minimalbestätigung gehört nur zum Minimalprofil.");
        Numbers.Require(!result.CycleControlPoint || ((result.Minimal || result.Digital) && result.parsed.ContainsKey("--confirm-ob1-automatic") && !result.parsed.ContainsKey("--partition")),
            "C-Profil nur separat im Minimal-/Digitaltest mit --confirm-ob1-automatic; --partition ist dabei nicht erlaubt.");
        Numbers.Require(result.CycleControlPoint || !result.parsed.ContainsKey("--confirm-ob1-automatic"), "OB1-Automatikbestätigung erfordert ausdrückliches C-Profil.");
        Numbers.Require(!result.Digital || (!result.Minimal && result.CycleControlPoint && result.Has("--confirm-tia-digital")), "Digitaltest braucht eigenes C-Profil und TIA-Digitalbestätigung.");
        Numbers.Require(!result.Has("--require-transport") || result.Has("--allow-movement"), "Transportabnahme benötigt bewusste Bewegungsfreigabe.");
        foreach (string key in new[] { "--confirm-tia-digital", "--allow-movement", "--require-transport", "--stream", "--profile", "--plant", "--max-virtual-seconds", "--wall-seconds" })
            Numbers.Require(result.Digital || !result.Has(key), "Argument gehört ausschließlich zum Digitalprofil: " + key);
        if (result.Digital) { result.Required("--profile"); result.Required("--plant"); }
        result.Required("--map"); if (!result.CycleControlPoint) result.Required("--partition"); result.Required("--report");
        return result;
    }
}
