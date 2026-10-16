using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace IisKeepAlive
{
    internal class Program
    {
        private static HttpClient? _httpClient;
        private static bool _logToFile = true;
        private static readonly string LogDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");

        static async Task Main(string[] args)
        {
            Console.Title = "IIS Keep-Alive Service";

            // 1. Build configuration
            var config = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build();

            var urls = new List<string>();
            config.GetSection("Settings:Urls").Bind(urls);

            // Command line arguments override
            if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
            {
                urls.Clear();
                urls.Add(args[0]);
            }

            if (urls.Count == 0)
            {
                urls.Add("http://localhost");
            }

            int intervalMinutes = config.GetValue("Settings:IntervalMinutes", 60);
            if (args.Length > 1 && int.TryParse(args[1], out int argInterval))
            {
                intervalMinutes = argInterval;
            }

            int timeoutSeconds = config.GetValue("Settings:TimeoutSeconds", 30);
            bool runOnce = config.GetValue("Settings:RunOnce", false);
            bool ignoreSsl = config.GetValue("Settings:IgnoreSslErrors", true);
            _logToFile = config.GetValue("Settings:LogToFile", true);

            // 2. Setup HttpClient
            var handler = new HttpClientHandler();
            if (ignoreSsl)
            {
                handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;
            }

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(timeoutSeconds)
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "IIS-KeepAlive-Worker/1.0");

            Log("=================================================");
            Log("  IIS Keep-Alive Service Started");
            Log($"  Target URLs    : {string.Join(", ", urls)}");
            Log($"  Interval       : Every {intervalMinutes} min");
            Log($"  Mode           : {(runOnce ? "Single Ping (RunOnce)" : "Continuous Loop")}");
            Log($"  Ignore SSL     : {ignoreSsl}");
            Log("=================================================");

            // 3. Execution loop
            do
            {
                foreach (var url in urls)
                {
                    await PingUrlAsync(url);
                }

                if (runOnce)
                {
                    Log("RunOnce is enabled. Exiting.");
                    break;
                }

                Log($"Next check in {intervalMinutes} minutes... (Press Ctrl+C to stop)");
                await Task.Delay(TimeSpan.FromMinutes(intervalMinutes));

            } while (!runOnce);
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
                // Silently ignore disk write errors
            }
        }
    }
}
