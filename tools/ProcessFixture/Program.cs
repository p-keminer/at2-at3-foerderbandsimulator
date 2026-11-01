using System;
using System.IO;
using System.Text;
using System.Threading;

// Harmless console child for watchdog tests. NO simulator/adapter/Siemens references.
internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        string Value(string name, string fallback)
        { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }
        int exit = int.Parse(Value("--exit-code", "0"));
        string map = Value("--map", "");
        if (Path.GetFileName(map).StartsWith("fail", StringComparison.Ordinal)) exit = 1;
        Console.WriteLine("SIEMENS_FREE_PROCESS_FIXTURE");
        for (int i = 0; i < args.Length; i++) Console.WriteLine("ARG" + i + ":" + Convert.ToBase64String(Encoding.UTF8.GetBytes(args[i])));
        Console.WriteLine("Prüfung äöü"); Console.Error.WriteLine("Fehlerprüfung äöü");
        int bulk = int.Parse(Value("--bulk-lines", "0"));
        for (int i = 0; i < bulk; i++) { Console.WriteLine(new string('x', 300)); Console.Error.WriteLine(new string('y', 300)); }
        Console.Out.Flush(); Console.Error.Flush();
        Thread.Sleep(int.Parse(Value("--delay-ms", "0")));
        string report = Value("--report", "");
        if (report.Length > 0)
        {
            string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
            File.WriteAllText(report, "{\"fixture\":true,\"result\":{\"Passed\":" + (exit == 0 ? "true" : "false") +
                "},\"map\":\"" + Escape(map) + "\",\"cwd\":\"" + Escape(Environment.CurrentDirectory) + "\"}", new UTF8Encoding(false));
        }
        return exit;
    }
}
