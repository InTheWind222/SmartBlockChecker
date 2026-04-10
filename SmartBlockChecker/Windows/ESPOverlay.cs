using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using System;
using System.Numerics;

namespace SmartBlockChecker.Windows;

internal sealed unsafe class ESPOverlay : Window, IDisposable
{
    private readonly BlacklistService _blacklistService;
    private readonly IObjectTable _objectTable;
    private readonly IGameGui _gameGui;
    private readonly IClientState _clientState;
    private readonly ITargetManager _targetManager;
    private readonly Configuration _config;
    private readonly IPluginLog _log;

    public ESPOverlay(
        BlacklistService blacklistService,
        IObjectTable objectTable,
        IGameGui gameGui,
        ITargetManager targetManager,
        Configuration config,
        IClientState clientState,
        IPluginLog log)
        : base("SmartBlockCheckerESP",
              ImGuiWindowFlags.NoInputs |
              ImGuiWindowFlags.NoNav |
              ImGuiWindowFlags.NoTitleBar |
              ImGuiWindowFlags.NoScrollbar |
              ImGuiWindowFlags.NoBackground)
    {
        _blacklistService = blacklistService;
        _objectTable = objectTable;
        _gameGui = gameGui;
        _targetManager = targetManager;
        _config = config;
        _clientState = clientState;
        _log = log;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(float.MaxValue, float.MaxValue),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };
        Position = Vector2.Zero;
    }

    public override void PreDraw()
    {
        Size = ImGui.GetIO().DisplaySize;
        Position = Vector2.Zero;
    }

    public void Dispose() { }

    public override void Draw()
    {
        if (!_config.ShowEspCircles && !_config.ShowTargetWarning) return;
        if (!_clientState.IsLoggedIn) return;

        try
        {
            if (_config.ShowTargetWarning)
            {
                var target = _targetManager.Target;
                if (target != null &&
                    target.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player &&
                    target is Dalamud.Game.ClientState.Objects.Types.ICharacter charTarget)
                {
                    var structTarget = (Character*)charTarget.Address;
                    if (structTarget != null)
                    {
                        if (_blacklistService.IsBlocked(
                            structTarget->ContentId,
                            structTarget->AccountId,
                            target.Name.TextValue))
                        {
                            DrawTargetWarning();
                        }
                    }
                }
            }

            if (_config.ShowEspCircles)
            {
                foreach (var obj in _objectTable)
                {
                    if (obj == null || !obj.IsValid()) continue;
                    if (obj.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player) continue;

                    var character = (Character*)obj.Address;
                    if (character == null) continue;

                    ulong contentId = character->ContentId;
                    ulong accountId = character->AccountId;
                    string playerName = obj.Name?.TextValue ?? string.Empty;

                    if (contentId != 0 || accountId != 0 || !string.IsNullOrWhiteSpace(playerName))
                    {
                        if (_blacklistService.IsBlocked(contentId, accountId, playerName))
                        {
                            DrawESPCircle(obj);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed while drawing the ESP overlay.");
        }
    }

    private void DrawTargetWarning()
    {
        var drawList = ImGui.GetBackgroundDrawList();
        var display = ImGui.GetIO().DisplaySize;

        float alpha = 0.9f;

        float bannerY = display.Y * 0.12f;
        float bannerH = 36f;
        var bannerMin = new Vector2(display.X * 0.3f, bannerY);
        var bannerMax = new Vector2(display.X * 0.7f, bannerY + bannerH);
        drawList.AddRectFilled(bannerMin, bannerMax,
            ImGui.ColorConvertFloat4ToU32(new Vector4(0.6f, 0.0f, 0.0f, 0.75f * alpha)), 6f);
        drawList.AddRect(bannerMin, bannerMax,
            ImGui.ColorConvertFloat4ToU32(new Vector4(1.0f, 0.3f, 0.3f, alpha)), 6f, ImDrawFlags.None, 2f);

        var text = "\u26a0  BLOCKED TARGET  \u26a0";
        var textSize = ImGui.CalcTextSize(text);
        var textPos = new Vector2(
            (display.X - textSize.X) / 2f,
            bannerY + (bannerH - textSize.Y) / 2f
        );

        drawList.AddText(textPos + new Vector2(1, 1),
            ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, alpha)), text);
        drawList.AddText(textPos,
            ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, alpha)), text);
    }

    private void DrawESPCircle(IGameObject obj)
    {
        var drawList = ImGui.GetBackgroundDrawList();
        var worldPos = obj.Position;
        if (worldPos == Vector3.Zero) return;

        try
        {
            if (_gameGui.WorldToScreen(worldPos, out var screenPos))
            {
                float dotRadius = _config.EspDotSize;
                drawList.AddCircleFilled(screenPos, dotRadius,
                    ImGui.ColorConvertFloat4ToU32(new Vector4(1, 0.1f, 0.1f, 1.0f)));

                drawList.AddCircle(screenPos, dotRadius,
                    ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 1.0f)), 16, 1.0f);

                var headPos = new Vector3(worldPos.X, worldPos.Y + 2.2f, worldPos.Z);
                if (_gameGui.WorldToScreen(headPos, out var headScreenPos))
                {
                    string playerName = obj.Name?.TextValue ?? "Unknown";
                    string text = $"\u26d4 BLOCKED: {playerName}";

                    // Scale the label based on projected height so it shrinks naturally at range
                    // instead of staying visually oversized as the player gets farther away.
                    float projectedHeight = MathF.Abs(headScreenPos.Y - screenPos.Y);
                    float distanceScale = Math.Clamp(projectedHeight / 90f, 0.55f, 1.0f);
                    float labelScale = _config.EspTextScale * distanceScale;
                    float pillPaddingX = 6f * distanceScale;
                    float pillPaddingY = 2f * distanceScale;
                    float pillRounding = 4f * distanceScale;

                    ImGui.SetWindowFontScale(labelScale);
                    var textSize = ImGui.CalcTextSize(text);
                    var textPos = new Vector2(headScreenPos.X - (textSize.X / 2), headScreenPos.Y);

                    var pillMin = textPos - new Vector2(pillPaddingX, pillPaddingY);
                    var pillMax = textPos + new Vector2(textSize.X + pillPaddingX, textSize.Y + pillPaddingY);
                    drawList.AddRectFilled(pillMin, pillMax,
                        ImGui.ColorConvertFloat4ToU32(new Vector4(0.15f, 0.0f, 0.0f, 0.85f)), pillRounding);
                    drawList.AddRect(pillMin, pillMax,
                        ImGui.ColorConvertFloat4ToU32(new Vector4(1.0f, 0.3f, 0.3f, 0.9f)), pillRounding, ImDrawFlags.None, 1f);

                    drawList.AddText(textPos,
                        ImGui.ColorConvertFloat4ToU32(new Vector4(1, 0.4f, 0.4f, 1)), text);

                    ImGui.SetWindowFontScale(1.0f);
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed while drawing a blocked-player ESP marker.");
        }
    }
}
