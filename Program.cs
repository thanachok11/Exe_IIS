using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace IisKeepAlive
{
    public class AppConfig
    {
        public SettingsConfig Settings { get; set; } = new();
    }

    public class SettingsConfig
    {
        public List<string> Urls { get; set; } = new() { "http://localhost" };
        public int IntervalMinutes { get; set; } = 60;
        public int TimeoutSeconds { get; set; } = 30;
        public bool RunOnce { get; set; } = false;
        public bool IgnoreSslErrors { get; set; } = true;
        public bool LogToFile { get; set; } = true;
    }

    internal class Program
    {
        private static HttpClient? _httpClient;
        private static bool _logToFile = true;
        private static readonly string LogDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");

        static async Task Main(string[] args)
        {
            Console.Title = "IIS Keep-Alive Service";

            // 1. Load config from appsettings.json using built-in System.Text.Json
            var settings = new SettingsConfig();
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

            if (File.Exists(configPath))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(configPath);
                    var loaded = JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (loaded?.Settings != null)
                    {
                        settings = loaded.Settings;
                    }
                }
                catch (Exception ex)
                {
                    Log($"[WARN] Could not parse appsettings.json: {ex.Message}. Using default settings.");
                }
            }

            // CLI arguments override
            if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
            {
                settings.Urls = new List<string> { args[0] };
            }

            if (args.Length > 1 && int.TryParse(args[1], out int argInterval))
            {
                settings.IntervalMinutes = argInterval;
            }

            if (settings.Urls == null || settings.Urls.Count == 0)
            {
                settings.Urls = new List<string> { "http://localhost" };
            }

            _logToFile = settings.LogToFile;

            // 2. Setup HttpClient
            var handler = new HttpClientHandler();
            if (settings.IgnoreSslErrors)
            {
                handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
            }

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds)
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "IIS-KeepAlive-Worker/1.0");

            Log("=================================================");
            Log("  IIS Keep-Alive Service Started");
            Log($"  Target URLs    : {string.Join(", ", settings.Urls)}");
            Log($"  Interval       : Every {settings.IntervalMinutes} min");
            Log($"  Mode           : {(settings.RunOnce ? "Single Ping (RunOnce)" : "Continuous Loop")}");
            Log($"  Ignore SSL     : {settings.IgnoreSslErrors}");
            Log("=================================================");

            // 3. Execution loop
            do
            {
                foreach (var url in settings.Urls)
                {
                    await PingUrlAsync(url);
                }

                if (settings.RunOnce)
                {
                    Log("RunOnce is enabled. Exiting.");
                    break;
                }

                Log($"Next check in {settings.IntervalMinutes} minutes... (Press Ctrl+C to stop)");
                await Task.Delay(TimeSpan.FromMinutes(settings.IntervalMinutes));

            } while (!settings.RunOnce);
        }

        private static async Task PingUrlAsync(string url)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                Log($"[PING] Requesting: {url}");
                using var response = await _httpClient!.GetAsync(url);
                sw.Stop();

                Log($"[SUCCESS] Status: {(int)response.StatusCode} {response.StatusCode} | Time: {sw.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                sw.Stop();
                Log($"[ERROR] Failed to reach {url} ({sw.ElapsedMilliseconds} ms): {ex.Message}");
            }
        }

        private static void Log(string message)
        {
            string formatted = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            Console.WriteLine(formatted);

            if (!_logToFile) return;

            try
            {
                if (!Directory.Exists(LogDir))
                {
                    Directory.CreateDirectory(LogDir);
                }

                string logFile = Path.Combine(LogDir, $"keepalive_{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(logFile, formatted + Environment.NewLine);
            }
            catch
            {
                // Ignore disk write errors
            }
        }
    }
}
