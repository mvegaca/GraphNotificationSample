using System;
using System.Diagnostics;

namespace GraphNotificationSample.Helpers
{
    public static class Logger
    {
        public static event EventHandler<string> LogUpdated;

        public static string AppLogs { get; set; } = string.Empty;

        public static void LogMessage(string message)
        {
            message = $"[{string.Format("{0:T}", DateTime.Now)}] {message}";
            Debug.WriteLine(message);
            AppLogs = message + Environment.NewLine + AppLogs;
            LogUpdated?.Invoke(null, message);
        }
    }
}
