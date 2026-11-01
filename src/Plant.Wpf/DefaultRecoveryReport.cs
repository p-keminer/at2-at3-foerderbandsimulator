using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace Plant.Wpf
{
    internal static class DefaultRecoveryReport
    {
        internal static string? ConfirmedState(string text, string expectedInstance = "AT2_Kopplung")
        {
            var data = new JavaScriptSerializer { MaxJsonLength = 1024 * 1024 }.DeserializeObject(text) as IDictionary<string, object>;
            if (data == null) return null;
            bool True(string key) => data.TryGetValue(key, out var value) && value is bool b && b;
            bool Equal(string key, string expected) => data.TryGetValue(key, out var value) && value is string s && s == expected;
            if (!True("passed") || !True("cleanupConfirmed") ||
                !Equal("operation", "ExplicitRestoreDefault") || !Equal("instance", expectedInstance) ||
                !Equal("observedName", expectedInstance) || !Equal("afterMode", "Default") ||
                !Equal("stage", "Completed") || !Equal("sdkProductVersion", "5.0.0.2")) return null;
            if (Equal("beforeState", "Stop"))
                return Equal("afterState", "Stop") && True("defaultAndStopObserved") ? "Stop" : null;
            return Equal("afterState", "Run") && True("defaultAndRunObserved") ? "Run" : null;
        }
    }
}
