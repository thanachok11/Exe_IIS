using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace IisKeepAlive
{
    public class AppConfig
    {
        public SettingsConfig Settings { get; set; } = new();
    }

    public class TargetRequestConfig
    {
        public string Url { get; set; } = string.Empty;
        public string Method { get; set; } = "GET";
        public Dictionary<string, string>? Headers { get; set; }
        public string? Body { get; set; }
        public string? ContentType { get; set; } = "application/json";
    }

    public class SettingsConfig
    {
        public List<string>? Urls { get; set; }
        public List<TargetRequestConfig>? Targets { get; set; }

        /// <summary>
        /// Local clock times to fire, e.g. ["06:59", "18:59"].
        /// Accepts one string ("06:59" or "06:59,18:59") or an array.
        /// When set, IntervalMinutes is not used.
        /// </summary>
        [JsonConverter(typeof(ScheduleTimesJsonConverter))]
        public List<string>? ScheduleTimes { get; set; }

        public int IntervalMinutes { get; set; } = 15;
        public int TimeoutSeconds { get; set; } = 90;
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
            Console.WriteLine("[INFO] Starting IIS Keep-Alive Service...");

            try
            {
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

                // Prepare targets: Targets take precedence, otherwise fallback to Urls
                var targetsToRun = new List<TargetRequestConfig>();

                if (settings.Targets != null && settings.Targets.Count > 0)
                {
                    targetsToRun.AddRange(settings.Targets.Where(t => !string.IsNullOrWhiteSpace(t.Url)));
                }
                else if (settings.Urls != null && settings.Urls.Count > 0)
                {
                    foreach (var url in settings.Urls.Where(u => !string.IsNullOrWhiteSpace(u)))
                    {
                        targetsToRun.Add(new TargetRequestConfig { Url = url, Method = "GET" });
                    }
                }

                // CLI arguments override
                if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
                {
                    targetsToRun = new List<TargetRequestConfig>
                    {
                        new TargetRequestConfig { Url = args[0], Method = "GET" }
                    };
                }

                if (args.Length > 1 && int.TryParse(args[1], out int argInterval))
                {
                    settings.IntervalMinutes = argInterval;
                }

                if (targetsToRun.Count == 0)
                {
                    targetsToRun.Add(new TargetRequestConfig { Url = "http://localhost", Method = "GET" });
                }

                _logToFile = settings.LogToFile;
                var schedule = ParseScheduleTimes(settings.ScheduleTimes);

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
                Log($"  Target Count   : {targetsToRun.Count}");
                foreach (var target in targetsToRun)
                {
                    string method = string.IsNullOrWhiteSpace(target.Method) ? "GET" : target.Method.ToUpperInvariant();
                    Log($"  -> [{method}] {target.Url}");
                }
                if (schedule.Count > 0)
                {
                    Log($"  Schedule      : {string.Join(", ", schedule.Select(t => t.ToString("HH:mm")))} (local time)");
                    Log("  Interval       : ignored while ScheduleTimes is set");
                }
                else
                {
                    Log($"  Interval       : Every {settings.IntervalMinutes} min");
                }
                Log($"  Timeout        : {settings.TimeoutSeconds} sec");
                Log($"  Mode           : {(settings.RunOnce ? "Single Ping (RunOnce)" : "Continuous Loop")}");
                Log($"  Ignore SSL     : {settings.IgnoreSslErrors}");
                Log("=================================================");

                // 3. Execution loop
                if (schedule.Count > 0)
                {
                    DateTime? lastFiredSlot = null;
                    do
                    {
                        var slot = ResolveNextSlot(schedule, DateTime.Now, lastFiredSlot);
                        if (slot.RunAt > DateTime.Now)
                        {
                            Log($"Next run at {slot.RunAt:yyyy-MM-dd HH:mm:ss} (local time). Press Ctrl+C to stop.");
                            await WaitUntilAsync(slot.RunAt);
                        }

                        foreach (var target in targetsToRun)
                        {
                            await PingTargetAsync(target);
                        }

                        lastFiredSlot = slot.ScheduledAt;

                        if (settings.RunOnce)
                        {
                            Log("RunOnce is enabled. Exiting.");
                            break;
                        }
                    } while (!settings.RunOnce);
                }
                else
                {
                    do
                    {
                        foreach (var target in targetsToRun)
                        {
                            await PingTargetAsync(target);
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
            }
            catch (Exception fatal)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[FATAL ERROR] An unexpected error occurred: {fatal.Message}");
                Console.WriteLine(fatal.ToString());
                Console.ResetColor();
                Console.WriteLine("\nPress Enter to exit...");
                Console.ReadLine();
            }
        }

        private readonly record struct ScheduleSlot(DateTime ScheduledAt, DateTime RunAt);

        private static List<TimeOnly> ParseScheduleTimes(IEnumerable<string>? rawTimes)
        {
            var result = new List<TimeOnly>();
            if (rawTimes == null)
                return result;

            foreach (var raw in rawTimes)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                var text = raw.Trim().Replace('.', ':');
                if (TimeOnly.TryParseExact(
                        text,
                        new[] { "H:mm", "HH:mm", "H:mm:ss", "HH:mm:ss" },
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var time))
                {
                    result.Add(time);
                }
                else
                {
                    Log($"[WARN] Skip invalid ScheduleTimes value '{raw}'. Use HH:mm such as 06:59.");
                }
            }

            return result.Distinct().OrderBy(t => t).ToList();
        }

        private static ScheduleSlot ResolveNextSlot(IReadOnlyList<TimeOnly> times, DateTime now, DateTime? lastFiredSlot)
        {
            for (var dayOffset = 0; dayOffset <= 8; dayOffset++)
            {
                var day = DateOnly.FromDateTime(now).AddDays(dayOffset);
                foreach (var time in times)
                {
                    var scheduledAt = day.ToDateTime(time);
                    if (lastFiredSlot.HasValue && scheduledAt <= lastFiredSlot.Value)
                        continue;
                    if (now >= scheduledAt.AddMinutes(1))
                        continue;

                    var runAt = scheduledAt > now ? scheduledAt : now;
                    return new ScheduleSlot(scheduledAt, runAt);
                }
            }

            var fallback = DateOnly.FromDateTime(now).AddDays(1).ToDateTime(times[0]);
            return new ScheduleSlot(fallback, fallback);
        }

        private static async Task WaitUntilAsync(DateTime target)
        {
            while (DateTime.Now < target)
            {
                var remaining = target - DateTime.Now;
                if (remaining <= TimeSpan.Zero)
                    return;

                var slice = remaining > TimeSpan.FromSeconds(15)
                    ? TimeSpan.FromSeconds(15)
                    : remaining;
                await Task.Delay(slice);
            }
        }

        private static async Task PingTargetAsync(TargetRequestConfig target)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string method = string.IsNullOrWhiteSpace(target.Method) ? "GET" : target.Method.ToUpperInvariant();
            try
            {
                Log($"[PING] Requesting [{method}]: {target.Url}");
                using var request = new HttpRequestMessage(new HttpMethod(method), target.Url);

                if (target.Headers != null)
                {
                    foreach (var kvp in target.Headers)
                    {
                        if (string.Equals(kvp.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                            continue;
                        request.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
                    }
                }

                if (!string.IsNullOrEmpty(target.Body) && (method == "POST" || method == "PUT" || method == "PATCH"))
                {
                    request.Content = new StringContent(target.Body, System.Text.Encoding.UTF8, target.ContentType ?? "application/json");
                }

                using var response = await _httpClient!.SendAsync(request);
                sw.Stop();

                int statusCode = (int)response.StatusCode;
                if (statusCode >= 200 && statusCode < 400)
                {
                    Log($"[SUCCESS] Status: {statusCode} {response.StatusCode} | Time: {sw.ElapsedMilliseconds} ms");
                }
                else if (statusCode == 401 || statusCode == 403)
                {
                    Log($"[WARMED UP] Status: {statusCode} {response.StatusCode} | Time: {sw.ElapsedMilliseconds} ms (Authentication required, but WCF Service and AppPool are active)");
                }
                else
                {
                    Log($"[RESP] Status: {statusCode} {response.StatusCode} | Time: {sw.ElapsedMilliseconds} ms");
                }
            }
            catch (TaskCanceledException ex) when (ex.CancellationToken == default)
            {
                sw.Stop();
                Log($"[TIMEOUT] Request to {target.Url} timed out after {sw.ElapsedMilliseconds} ms. (Hint: Cold start compiling WCF or connecting DB can take 30-60s on first hit. Try increasing 'TimeoutSeconds' in appsettings.json)");
            }
            catch (Exception ex)
            {
                sw.Stop();
                Log($"[ERROR] Failed to reach {target.Url} ({sw.ElapsedMilliseconds} ms): {ex.Message}");
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

    internal sealed class ScheduleTimesJsonConverter : JsonConverter<List<string>?>
    {
        public override List<string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType == JsonTokenType.String)
            {
                var value = reader.GetString();
                if (string.IsNullOrWhiteSpace(value))
                    return new List<string>();

                return value
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
            }

            if (reader.TokenType != JsonTokenType.StartArray)
                throw new JsonException("ScheduleTimes must be a time string or an array of time strings.");

            var list = new List<string>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType == JsonTokenType.String)
                    list.Add(reader.GetString() ?? string.Empty);
                else
                    throw new JsonException("ScheduleTimes array items must be strings.");
            }

            return list;
        }

        public override void Write(Utf8JsonWriter writer, List<string>? value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStartArray();
            foreach (var item in value)
                writer.WriteStringValue(item);
            writer.WriteEndArray();
        }
    }
}
