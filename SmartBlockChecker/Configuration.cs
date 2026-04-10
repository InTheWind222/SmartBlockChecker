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
    public bool NotifyWhenBlockedNearby { get; set; } = false;

    public float EspDotSize { get; set; } = 3.5f;
    public float EspTextScale { get; set; } = 1.0f;
    public float NearbyNotificationRange { get; set; } = 60.0f;

    public int BlacklistHotkey { get; set; } = 0; // 0 = disabled

    [NonSerialized]
    private IDalamudPluginInterface? PluginInterface;

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        PluginInterface = pluginInterface;
    }

    public void Save()
    {
        PluginInterface!.SavePluginConfig(this);
    }
}
