using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

// Reads bytes and ECMA-335 metadata. Does not load or execute the inspected assembly.
using var stream = File.OpenRead(args[0]);
using var pe = new PEReader(stream);
var md = pe.GetMetadataReader();
Console.WriteLine($"Machine={pe.PEHeaders.CoffHeader.Machine}; CorFlags={pe.PEHeaders.CorHeader?.Flags}; Metadata={md.MetadataVersion}");
Console.WriteLine("References: " + string.Join(", ", md.AssemblyReferences.Select(h => { var a = md.GetAssemblyReference(h); return md.GetString(a.Name) + " " + a.Version; })));
foreach (var h in md.TypeDefinitions)
{
    var t = md.GetTypeDefinition(h); string name = md.GetString(t.Name);
    if (name.Contains("Instance") || name.Contains("Area") || name.Contains("SyncPoint") || name.Contains("SimulationRuntime") || name.StartsWith("Delegate_") || name == "EOperatingMode" || name == "EOperatingState" || name == "ERuntimeErrorCode")
    {
        Console.WriteLine(md.GetString(t.Namespace) + "." + name);
        foreach (var mh in t.GetMethods())
        {
            var m = md.GetMethodDefinition(mh); string n = md.GetString(m.Name);
            if (args.Length > 1 || name.Contains("Exception") || n.Contains("Read") || n.Contains("Write") || n.Contains("Sync") || n.Contains("Freeze") || n.Contains("Time"))
                Console.WriteLine("  " + n + " blob=" + Convert.ToHexString(md.GetBlobBytes(m.Signature)));
        }
        if (args.Length > 1)
            foreach (var fh in t.GetFields())
            {
                var f = md.GetFieldDefinition(fh);
                Console.WriteLine("  field " + md.GetString(f.Name) + " blob=" + Convert.ToHexString(md.GetBlobBytes(f.Signature)));
            }
    }
}
