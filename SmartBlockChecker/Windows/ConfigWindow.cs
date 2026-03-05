using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace SmartBlockChecker.Windows;

internal sealed unsafe class ConfigWindow : Window, IDisposable
{
    private readonly Plugin _plugin;
    private readonly Configuration _config;
    private readonly BlacklistChecker _checker;
    private readonly IObjectTable _objectTable;
    private readonly IClientState _clientState;

    private List<BlockedPlayerInfo> _cachedBlocked = new();
    private int _refreshCounter = 0;
    private const int RefreshInterval = 120;

    private static readonly Vector4 ColorRed         = new(1.0f, 0.30f, 0.30f, 1.0f);
    private static readonly Vector4 ColorGreen       = new(0.30f, 1.0f, 0.30f, 1.0f);
    private static readonly Vector4 ColorYellow      = new(1.0f, 0.85f, 0.30f, 1.0f);
    private static readonly Vector4 ColorDimText     = new(0.60f, 0.60f, 0.60f, 1.0f);
    private static readonly Vector4 ColorHeader      = new(0.85f, 0.40f, 0.40f, 1.0f);
    private static readonly Vector4 ColorNearby      = new(1.0f, 0.55f, 0.20f, 1.0f);

    public ConfigWindow(Plugin plugin, BlacklistChecker checker, IObjectTable objectTable, IClientState clientState)
        : base("Smart Block Checker##ConfigWindow")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 380),
            MaximumSize = new Vector2(600, 800)
        };

        _plugin = plugin;
        _config = plugin.Configuration;
        _checker = checker;
        _objectTable = objectTable;
        _clientState = clientState;
    }

    public void Dispose() { }

    public override void Draw()
    {
        _refreshCounter++;
        if (_refreshCounter >= RefreshInterval)
        {
            _refreshCounter = 0;
            _cachedBlocked = _checker.GetBlockedEntries();
        }

        ImGui.PushStyleColor(ImGuiCol.Text, ColorHeader);
        ImGui.TextUnformatted("\u26a0 Smart Block Checker");
        ImGui.PopStyleColor();

        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, ColorDimText);
        ImGui.TextUnformatted("v1.0");
        ImGui.PopStyleColor();

        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.BeginTabBar("##BlockCheckerTabs"))
        {
            if (ImGui.BeginTabItem("\u2699 Settings"))
            {
                DrawSettingsTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("\ud83d\udeab Blocked Players"))
            {
                DrawBlockedPlayersTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("\ud83d\udc41 Nearby"))
            {
                DrawNearbyTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private bool _isRecordingHotkey = false;

    private void DrawSettingsTab()
    {
        ImGui.Spacing();

        ImGui.PushStyleColor(ImGuiCol.Text, ColorYellow);
        ImGui.TextUnformatted("Blacklist Actions");
        ImGui.PopStyleColor();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Button("Blacklist Current Target", new Vector2(-1, 30)))
        {
            _plugin.ExecuteBlacklistTarget();
        }
        ImGui.PushStyleColor(ImGuiCol.Text, ColorDimText);
        ImGui.TextWrapped("Instantly blacklists whoever you are currently targeting.");
        ImGui.PopStyleColor();

        ImGui.Spacing();

        ImGui.TextUnformatted("Quick Blacklist Hotkey:");
        ImGui.SameLine();
        
        string hotkeyName = _config.BlacklistHotkey == 0 ? "None" : ((Dalamud.Game.ClientState.Keys.VirtualKey)_config.BlacklistHotkey).ToString();
        if (_isRecordingHotkey) hotkeyName = "Press a key...";

        if (ImGui.Button($"{hotkeyName}##HotkeyRecord", new Vector2(150, 0)))
        {
            _isRecordingHotkey = true;
        }

        if (_isRecordingHotkey)
        {
            for (int vk = 1; vk < 255; vk++)
            {
                if ((GetAsyncKeyState(vk) & 0x8000) != 0)
                {
                    _config.BlacklistHotkey = vk;
                    _config.Save();
                    _isRecordingHotkey = false;
                    break;
                }
            }
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                _isRecordingHotkey = false;
            }
        }

        if (ImGui.Button("Clear Hotkey"))
        {
            _config.BlacklistHotkey = 0;
            _config.Save();
        }

        ImGui.PushStyleColor(ImGuiCol.Text, ColorDimText);
        ImGui.TextWrapped("Click the button and press a key to set it as your quick-blacklist shortcut.");
        ImGui.PopStyleColor();

        ImGui.Spacing();
        ImGui.Spacing();

        ImGui.PushStyleColor(ImGuiCol.Text, ColorYellow);
        ImGui.TextUnformatted("Action Prevention");
        ImGui.PopStyleColor();
        ImGui.Separator();
        ImGui.Spacing();

        bool preventActions = _config.PreventBlockedActions;
        if (ImGui.Checkbox("Block actions on blacklisted targets", ref preventActions))
        {
            _config.PreventBlockedActions = preventActions;
            _config.Save();
        }
        ImGui.PushStyleColor(ImGuiCol.Text, ColorDimText);
        ImGui.TextWrapped("Prevents heals, raises, and other targeted actions from firing on blocked players, saving your GCD.");
        ImGui.PopStyleColor();

        ImGui.Spacing();
        ImGui.Spacing();

        ImGui.PushStyleColor(ImGuiCol.Text, ColorYellow);
        ImGui.TextUnformatted("Visual Indicators");
        ImGui.PopStyleColor();
        ImGui.Separator();
        ImGui.Spacing();

        bool showOverlay = _config.ShowEspCircles;
        if (ImGui.Checkbox("ESP indicators on blocked players", ref showOverlay))
        {
            _config.ShowEspCircles = showOverlay;
            _config.Save();
        }
        ImGui.PushStyleColor(ImGuiCol.Text, ColorDimText);
        ImGui.TextWrapped("Draws a static red dot and name tag on any blocked player visible in the world.");
        ImGui.PopStyleColor();

        if (_config.ShowEspCircles)
        {
            ImGui.Indent(20f);
            
            float dotSize = _config.EspDotSize;
            if (ImGui.SliderFloat("Dot Size", ref dotSize, 1.0f, 20.0f, "%.1f"))
            {
                _config.EspDotSize = dotSize;
                _config.Save();
            }

            float textScale = _config.EspTextScale;
            if (ImGui.SliderFloat("Text Scale", ref textScale, 0.5f, 3.0f, "%.1f"))
            {
                _config.EspTextScale = textScale;
                _config.Save();
            }

            ImGui.Unindent(20f);
        }

        ImGui.Spacing();

        bool showTarget = _config.ShowTargetWarning;
        if (ImGui.Checkbox("\"BLOCKED\" warning on target bar", ref showTarget))
        {
            _config.ShowTargetWarning = showTarget;
            _config.Save();
        }
        ImGui.PushStyleColor(ImGuiCol.Text, ColorDimText);
        ImGui.TextWrapped("Shows a large red warning banner when you target a blocked player.");
        ImGui.PopStyleColor();

        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.PushStyleColor(ImGuiCol.Text, ColorDimText);
        ImGui.TextUnformatted($"Tracking {_cachedBlocked.Count} blocked player(s).");
        ImGui.PopStyleColor();
    }

    private void DrawBlockedPlayersTab()
    {
        ImGui.Spacing();

        if (_cachedBlocked.Count == 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ColorDimText);
            ImGui.TextWrapped("No blocked players found. Your blacklist is either empty or hasn't loaded yet. Try switching zones or waiting a moment.");
            ImGui.PopStyleColor();
            return;
        }

        ImGui.PushStyleColor(ImGuiCol.Text, ColorRed);
        ImGui.TextUnformatted($"{_cachedBlocked.Count} Blocked Player(s)");
        ImGui.PopStyleColor();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.BeginTable("##BlockedTable", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable,
            new Vector2(0, ImGui.GetContentRegionAvail().Y - 30)))
        {
            ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 30f);
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            for (int i = 0; i < _cachedBlocked.Count; i++)
            {
                var entry = _cachedBlocked[i];

                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                ImGui.PushStyleColor(ImGuiCol.Text, ColorDimText);
                ImGui.TextUnformatted($"{i + 1}");
                ImGui.PopStyleColor();

                ImGui.TableSetColumnIndex(1);
                ImGui.PushStyleColor(ImGuiCol.Text, ColorRed);
                ImGui.TextUnformatted(entry.Name);
                ImGui.PopStyleColor();
            }

            ImGui.EndTable();
        }

        ImGui.PushStyleColor(ImGuiCol.Text, ColorDimText);
        ImGui.TextUnformatted("Data refreshes every ~2 seconds.");
        ImGui.PopStyleColor();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.PushStyleColor(ImGuiCol.Text, ColorDimText);
        ImGui.TextWrapped($"Diag: {_checker.DiagnosticInfo}");
        ImGui.PopStyleColor();
    }

    private void DrawNearbyTab()
    {
        ImGui.Spacing();

        if (!_clientState.IsLoggedIn)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ColorDimText);
            ImGui.TextWrapped("Not logged in. Enter the game to scan for nearby blocked players.");
            ImGui.PopStyleColor();
            return;
        }

        var nearbyPlayers = new List<(IGameObject Obj, string Name, float Distance, bool Blocked)>();

        foreach (var obj in _objectTable)
        {
            if (obj == null || !obj.IsValid()) continue;
            if (obj.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player) continue;

            var character = (Character*)obj.Address;
            if (character == null) continue;

            ulong contentId = character->ContentId;
            ulong accountId = character->AccountId;

            bool isBlocked = _checker.IsBlocked(contentId, accountId);
            
            float distance = 0f;
            try
            {
                var localPlayer = _objectTable.LocalPlayer;
                if (localPlayer != null)
                {
                    distance = Vector3.Distance(localPlayer.Position, obj.Position);
                }
            }
            catch { }

            var name = obj.Name?.TextValue ?? "<Unknown>";
            nearbyPlayers.Add((obj, name, distance, isBlocked));
        }

        if (nearbyPlayers.Count == 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ColorGreen);
            ImGui.TextUnformatted("\u2714 No players nearby.");
            ImGui.PopStyleColor();
            return;
        }

        ImGui.PushStyleColor(ImGuiCol.Text, ColorNearby);
        ImGui.TextUnformatted($"\ud83d\udc41 Players Nearby");
        ImGui.PopStyleColor();
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.BeginTable("##NearbyTable", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg,
            new Vector2(0, 0)))
        {
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Dist", ImGuiTableColumnFlags.WidthFixed, 50f);
            ImGui.TableSetupColumn("Action", ImGuiTableColumnFlags.WidthFixed, 80f);
            ImGui.TableHeadersRow();

            foreach (var (obj, name, dist, isBlocked) in nearbyPlayers)
            {
                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                if (isBlocked) ImGui.PushStyleColor(ImGuiCol.Text, ColorRed);
                ImGui.TextUnformatted(name);
                if (isBlocked) ImGui.PopStyleColor();

                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted($"{dist:F1}y");

                ImGui.TableSetColumnIndex(2);
                if (isBlocked)
                {
                    ImGui.TextUnformatted("Blocked");
                }
                else
                {
                    if (ImGui.Button($"Block##{name}_{obj.Address.ToInt64()}"))
                    {
                        _plugin.BlacklistByObject(obj);
                    }
                }
            }

            ImGui.EndTable();
        }
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
