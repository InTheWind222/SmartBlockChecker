using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game.ClientState.Objects.Types;
using SmartBlockChecker.Hooking;
using SmartBlockChecker.Windows;
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

    public Configuration Configuration { get; init; }

    private readonly BlacklistService _blacklistService;
    private readonly HookController _hookController;
    private bool _hotkeyWasDown;

    private readonly WindowSystem _windowSystem = new("SmartBlockChecker");
    private ConfigWindow ConfigWindow { get; init; }
    private ESPOverlay EspOverlay { get; init; }

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
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        Framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        Framework.Update -= OnUpdate;
        PluginInterface.UiBuilder.Draw -= _windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        
        _windowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();
        EspOverlay.Dispose();

        _hookController.Dispose();
        CommandManager.RemoveHandler(ConfigCommand);
        CommandManager.RemoveHandler(QuickBlockCommand);
    }

    private void OnUpdate(IFramework framework)
    {
        if (Configuration.BlacklistHotkey <= 0 || !ClientState.IsLoggedIn)
        {
            _hotkeyWasDown = false;
            return;
        }

        var isDown = (GetAsyncKeyState(Configuration.BlacklistHotkey) & 0x8000) != 0;
        if (isDown && !_hotkeyWasDown)
        {
            ExecuteBlacklistTarget();
        }

        _hotkeyWasDown = isDown;
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

    public unsafe void ExecuteBlacklistTarget()
    {
        TryBlacklistTarget(TargetManager.Target);
    }

    public unsafe void TryBlacklistTarget(IGameObject? target)
    {
        if (target == null || target.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player)
        {
            PrintError("You must have a player targeted to blacklist.");
            return;
        }

        var character = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)target.Address;
        if (character == null)
        {
            PrintError("The selected target could not be read.");
            return;
        }

        ulong contentId = character->ContentId;
        ulong accountId = character->AccountId;

        if (contentId == 0 && accountId == 0)
        {
            PrintError("Target has no valid identifiers to blacklist.");
            return;
        }

        string name = target.Name.TextValue;

        if (_blacklistService.IsBlocked(contentId, accountId))
        {
            PrintError($"{name} is already blacklisted.");
            return;
        }

        bool success = _blacklistService.ExecuteBlacklistCommand();
        if (success)
        {
            PrintMessage($"Added {name} to the blacklist.");
        }
        else
        {
            PrintError($"Failed to add {name} to the blacklist. Check the Dalamud log for details.");
        }
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();

    private static void PrintMessage(string message)
    {
        ChatGui.Print($"[SmartBlock] {message}");
    }

    private static void PrintError(string message)
    {
        ChatGui.PrintError($"[SmartBlock] {message}");
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
