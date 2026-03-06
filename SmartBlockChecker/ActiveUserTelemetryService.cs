using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;

namespace SmartBlockChecker;

internal sealed class ActiveUserTelemetryService : IDisposable
{
    private const string DefaultTelemetryEndpoint = "";
    private static readonly TimeSpan ReportInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private readonly Configuration _configuration;
    private readonly IPluginLog _log;
    private readonly HttpClient _httpClient;
    private readonly string _pluginVersion;

    private int _reportInFlight;
    private bool _hasReportedThisSession;

    public ActiveUserTelemetryService(Configuration configuration, IPluginLog log, string pluginVersion)
    {
        _configuration = configuration;
        _log = log;
        _pluginVersion = pluginVersion;
        _httpClient = new HttpClient
        {
            Timeout = RequestTimeout
        };

        if (string.IsNullOrWhiteSpace(_configuration.AnonymousInstallId))
        {
            _configuration.AnonymousInstallId = Guid.NewGuid().ToString("N");
            _configuration.Save();
        }

        if (string.IsNullOrWhiteSpace(_configuration.TelemetryEndpoint))
        {
            _configuration.TelemetryEndpoint = DefaultTelemetryEndpoint;
        }
    }

    public int LastKnownActiveUsers => _configuration.LastKnownActiveUserCount;

    public string LastTelemetryStatus => _configuration.LastTelemetryStatus;

    public string Endpoint => _configuration.TelemetryEndpoint;

    public bool Enabled => _configuration.TelemetryEnabled;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(GetEndpoint());

    public void TryReport()
    {
        StartReport(force: false);
    }

    public void ForceReport()
    {
        StartReport(force: true);
    }

    private void StartReport(bool force)
    {
        if (!_configuration.TelemetryEnabled)
        {
            return;
        }

        var endpoint = GetEndpoint();
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            if (_configuration.LastTelemetryStatus != "Telemetry endpoint is not configured.")
            {
                _configuration.LastTelemetryStatus = "Telemetry endpoint is not configured.";
                _configuration.Save();
            }
            return;
        }

        if (!force && _hasReportedThisSession && !ShouldReportNow())
        {
            return;
        }

        if (Interlocked.Exchange(ref _reportInFlight, 1) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                if (!force && _hasReportedThisSession && !ShouldReportNow())
                {
                    return;
                }

                if (!force && !ShouldReportNow())
                {
                    _hasReportedThisSession = true;
                    return;
                }

                await ReportAsync(endpoint).ConfigureAwait(false);
                _hasReportedThisSession = true;
            }
            finally
            {
                Interlocked.Exchange(ref _reportInFlight, 0);
            }
        });
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private async Task ReportAsync(string endpoint)
    {
        try
        {
            var request = new TelemetryPingRequest
            {
                InstallId = _configuration.AnonymousInstallId,
                Plugin = "SmartBlockChecker",
                Version = _pluginVersion
            };

            using var message = new HttpRequestMessage(HttpMethod.Post, $"{endpoint.TrimEnd('/')}/v1/ping")
            {
                Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json")
            };

            using var response = await _httpClient.SendAsync(message).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var result = JsonSerializer.Deserialize<TelemetryPingResponse>(body);

            if (result?.ActiveUsers is int activeUsers)
            {
                _configuration.LastKnownActiveUserCount = activeUsers;
            }

            _configuration.LastTelemetryReportUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            _configuration.LastTelemetryStatus = $"Last reported {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC";
            _configuration.Save();
        }
        catch (Exception ex)
        {
            _configuration.LastTelemetryStatus = "Telemetry report failed.";
            _configuration.Save();
            _log.Warning(ex, "Active-user telemetry report failed.");
        }
    }

    private bool ShouldReportNow()
    {
        if (_configuration.LastTelemetryReportUnixSeconds <= 0)
        {
            return true;
        }

        var lastReport = DateTimeOffset.FromUnixTimeSeconds(_configuration.LastTelemetryReportUnixSeconds);
        return DateTimeOffset.UtcNow - lastReport >= ReportInterval;
    }

    private string GetEndpoint()
    {
        return string.IsNullOrWhiteSpace(_configuration.TelemetryEndpoint)
            ? DefaultTelemetryEndpoint
            : _configuration.TelemetryEndpoint;
    }

    private sealed class TelemetryPingRequest
    {
        public string InstallId { get; set; } = string.Empty;

        public string Plugin { get; set; } = string.Empty;

        public string Version { get; set; } = string.Empty;
    }

    private sealed class TelemetryPingResponse
    {
        public bool Ok { get; set; }

        public int ActiveUsers { get; set; }

        public int ActiveWindowDays { get; set; }
    }
}
