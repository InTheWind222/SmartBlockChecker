using Dalamud.Configuration;
using Dalamud.Plugin;
using System;

namespace SmartBlockChecker;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool PreventBlockedActions { get; set; } = true;
    public bool ShowEspCircles { get; set; } = true;
    public bool ShowTargetWarning { get; set; } = true;

    public float EspDotSize { get; set; } = 3.5f;
    public float EspTextScale { get; set; } = 1.0f;

    public int BlacklistHotkey { get; set; } = 0; // 0 = disabled
    public bool TelemetryEnabled { get; set; } = true;
    public string AnonymousInstallId { get; set; } = string.Empty;
    public long LastTelemetryReportUnixSeconds { get; set; } = 0;
    public int LastKnownActiveUserCount { get; set; } = 0;
    public string LastTelemetryStatus { get; set; } = "Not reported yet.";
    public string TelemetryEndpoint { get; set; } = string.Empty;

    [NonSerialized]
    private IDalamudPluginInterface? PluginInterface;

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        PluginInterface = pluginInterface;
        if (string.IsNullOrWhiteSpace(AnonymousInstallId))
        {
            AnonymousInstallId = Guid.NewGuid().ToString("N");
        }
    }

    public void Save()
    {
        PluginInterface!.SavePluginConfig(this);
    }
}
