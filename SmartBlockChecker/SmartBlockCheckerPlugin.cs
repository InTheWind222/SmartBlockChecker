using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game.ClientState.Objects.Types;
using SmartBlockChecker.Hooking;
using SmartBlockChecker.Windows;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

namespace SmartBlockChecker;

public sealed class SmartBlockCheckerPlugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;

    private const string ConfigCommand = "/blockchecker";
    private const string QuickBlockCommand = "/smartblock";
    private static readonly TimeSpan NearbyAlertScanInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan NearbyLeaveGracePeriod = TimeSpan.FromSeconds(5);

    public Configuration Configuration { get; init; }

    private readonly BlacklistService _blacklistService;
    private readonly HookController _hookController;
    private bool _hotkeyWasDown;
    private bool _loggedInvalidHotkey;
    private readonly HashSet<string> _notifiedNearbyAlerts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, NearbyAlertState> _trackedNearbyPlayers = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastNearbyAlertScanUtc = DateTime.MinValue;

    private readonly WindowSystem _windowSystem = new("SmartBlockChecker");
    private ConfigWindow ConfigWindow { get; init; }
    private ESPOverlay EspOverlay { get; init; }

    private sealed class NearbyAlertState
    {
        public string DisplayName { get; set; } = "A blocked player";
        public DateTime LastSeenUtc { get; set; }
        public bool HasAnnouncedLeft { get; set; }
    }

    public SmartBlockCheckerPlugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface);

        _blacklistService = new BlacklistService(Log, GameInteropProvider);
        _hookController = new HookController(GameInteropProvider, Log, _blacklistService, Configuration);
        _hookController.Initialize();

        ConfigWindow = new ConfigWindow(this, _blacklistService, ObjectTable, ClientState);
        EspOverlay = new ESPOverlay(_blacklistService, ObjectTable, GameGui, TargetManager, Configuration, ClientState, Log);

        _windowSystem.AddWindow(ConfigWindow);
        _windowSystem.AddWindow(EspOverlay);
        EspOverlay.IsOpen = true;

        RegisterCommands();
        PluginInterface.UiBuilder.Draw += _windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += OpenConfigUi;
        Framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        Framework.Update -= OnUpdate;
        PluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUi;
        
        _windowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();
        EspOverlay.Dispose();

        _hookController.Dispose();
        CommandManager.RemoveHandler(ConfigCommand);
        CommandManager.RemoveHandler(QuickBlockCommand);
    }

    private void OnUpdate(IFramework framework)
    {
        if (!ClientState.IsLoggedIn)
        {
            _hotkeyWasDown = false;
            _loggedInvalidHotkey = false;
            _notifiedNearbyAlerts.Clear();
            _trackedNearbyPlayers.Clear();
            _lastNearbyAlertScanUtc = DateTime.MinValue;
            return;
        }

        if (Configuration.BlacklistHotkey <= 0)
        {
            _hotkeyWasDown = false;
            _loggedInvalidHotkey = false;
        }
        else if (IsMouseHotkey(Configuration.BlacklistHotkey))
        {
            if (!_loggedInvalidHotkey)
            {
                Log.Warning("Ignoring invalid quick-blacklist hotkey {Hotkey}. Open /blockchecker and set a keyboard key instead.", Configuration.BlacklistHotkey);
                _loggedInvalidHotkey = true;
            }

            _hotkeyWasDown = false;
        }
        else
        {
            _loggedInvalidHotkey = false;

            var isDown = (GetAsyncKeyState(Configuration.BlacklistHotkey) & 0x8000) != 0;
            if (isDown && !_hotkeyWasDown)
            {
                ExecuteBlacklistTarget(showErrors: false);
            }

            _hotkeyWasDown = isDown;
        }

        UpdateNearbyNotifications();
    }

    private void RegisterCommands()
    {
        CommandManager.AddHandler(ConfigCommand, new CommandInfo((_, _) => ToggleConfigUi())
        {
            HelpMessage = "Open the Smart Block Checker configuration window."
        });

        CommandManager.AddHandler(QuickBlockCommand, new CommandInfo((_, _) => ExecuteBlacklistTarget())
        {
            HelpMessage = "Instantly blacklist your current target."
        });
    }

    public unsafe bool ExecuteBlacklistTarget(bool showErrors = true)
    {
        return TryBlacklistTarget(TargetManager.Target, showErrors);
    }

    public unsafe bool TryBlacklistTarget(IGameObject? target, bool showErrors = true)
    {
        if (target == null || target.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player)
        {
            if (showErrors)
            {
                PrintError("You must have a player targeted to blacklist.");
            }
            return false;
        }

        var character = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)target.Address;
        if (character == null)
        {
            if (showErrors)
            {
                PrintError("The selected target could not be read.");
            }
            return false;
        }

        ulong contentId = character->ContentId;
        ulong accountId = character->AccountId;

        if (contentId == 0 && accountId == 0)
        {
            if (showErrors)
            {
                PrintError("Target has no valid identifiers to blacklist.");
            }
            return false;
        }

        string name = target.Name.TextValue;

        if (_blacklistService.IsBlocked(contentId, accountId, name))
        {
            if (showErrors)
            {
                PrintError($"{name} is already blacklisted.");
            }
            return false;
        }

        bool success = _blacklistService.ExecuteBlacklistCommand();
        if (success)
        {
            PrintMessage($"Added {name} to the blacklist.");
            return true;
        }

        if (showErrors)
        {
            PrintError($"Failed to add {name} to the blacklist. Check the Dalamud log for details.");
        }
        return false;
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void OpenConfigUi() => ConfigWindow.IsOpen = true;

    private static void PrintMessage(string message)
    {
        ChatGui.Print($"[SmartBlock] {message}");
    }

    private static void PrintError(string message)
    {
        ChatGui.PrintError($"[SmartBlock] {message}");
    }

    private static bool IsMouseHotkey(int virtualKey)
    {
        return virtualKey is 1 or 2 or 4 or 5 or 6;
    }

    private unsafe void UpdateNearbyNotifications()
    {
        if (!Configuration.NotifyWhenBlockedNearby && !Configuration.NotifyWhenBlockedLeavesOrReturns)
        {
            return;
        }

        if (DateTime.UtcNow - _lastNearbyAlertScanUtc < NearbyAlertScanInterval)
        {
            return;
        }

        _lastNearbyAlertScanUtc = DateTime.UtcNow;
        var now = _lastNearbyAlertScanUtc;

        var localPlayer = ObjectTable.LocalPlayer;
        if (localPlayer == null)
        {
            return;
        }

        float notificationRange = Math.Clamp(Configuration.NearbyNotificationRange, 5.0f, 200.0f);
        var currentlyNearby = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var obj in ObjectTable)
        {
            if (obj == null || !obj.IsValid())
            {
                continue;
            }

            if (obj.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player)
            {
                continue;
            }

            if (obj.GameObjectId == localPlayer.GameObjectId)
            {
                continue;
            }

            float distance = Vector3.Distance(localPlayer.Position, obj.Position);
            if (distance > notificationRange)
            {
                continue;
            }

            var character = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)obj.Address;
            if (character == null)
            {
                continue;
            }

            ulong contentId = character->ContentId;
            ulong accountId = character->AccountId;
            string playerName = obj.Name?.TextValue ?? string.Empty;
            if (!_blacklistService.IsBlocked(contentId, accountId, playerName))
            {
                continue;
            }

            string alertKey = BuildNearbyAlertKey(contentId, accountId, playerName);
            if (string.IsNullOrEmpty(alertKey))
            {
                continue;
            }

            currentlyNearby.Add(alertKey);

            string displayName = string.IsNullOrWhiteSpace(playerName) ? "A blocked player" : playerName;
            if (!_trackedNearbyPlayers.TryGetValue(alertKey, out var alertState))
            {
                alertState = new NearbyAlertState();
                _trackedNearbyPlayers[alertKey] = alertState;
            }

            bool wasAway = alertState.HasAnnouncedLeft;
            alertState.DisplayName = displayName;
            alertState.LastSeenUtc = now;
            alertState.HasAnnouncedLeft = false;

            if (Configuration.NotifyWhenBlockedNearby && _notifiedNearbyAlerts.Add(alertKey))
            {
                PrintMessage($"{displayName} is nearby ({distance:F1}y).");
            }

            if (Configuration.NotifyWhenBlockedLeavesOrReturns && wasAway)
            {
                PrintMessage($"{displayName} came back nearby ({distance:F1}y).");
            }
        }

        if (!Configuration.NotifyWhenBlockedLeavesOrReturns)
        {
            return;
        }

        foreach (var (alertKey, alertState) in _trackedNearbyPlayers)
        {
            if (currentlyNearby.Contains(alertKey) || alertState.HasAnnouncedLeft)
            {
                continue;
            }

            if (now - alertState.LastSeenUtc < NearbyLeaveGracePeriod)
            {
                continue;
            }

            PrintMessage($"{alertState.DisplayName} left the area.");
            alertState.HasAnnouncedLeft = true;
        }
    }

    private static string BuildNearbyAlertKey(ulong contentId, ulong accountId, string? playerName)
    {
        if (contentId != 0)
        {
            return $"content:{contentId:X16}";
        }

        if (accountId != 0)
        {
            return $"account:{accountId:X16}";
        }

        string normalizedName = NormalizeName(playerName);
        return string.IsNullOrEmpty(normalizedName) ? string.Empty : $"name:{normalizedName}";
    }

    private static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        string trimmed = name.Trim();
        int worldSeparator = trimmed.IndexOf('@');
        if (worldSeparator >= 0)
        {
            trimmed = trimmed[..worldSeparator];
        }

        return trimmed.Replace("  ", " ", StringComparison.Ordinal).Trim();
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
