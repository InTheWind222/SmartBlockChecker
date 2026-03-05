using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using SmartBlockChecker.Windows;
using SmartBlockChecker.Hooking;

namespace SmartBlockChecker;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;

    private const string CommandName = "/blockchecker";
    private const string SmartBlockCommand = "/smartblock";

    public Configuration Configuration { get; init; }
    
    private readonly BlacklistChecker _blacklistChecker;
    private readonly HookHandler _hookHandler;

    public readonly WindowSystem WindowSystem = new("SmartBlockChecker");
    private ConfigWindow ConfigWindow { get; init; }
    private ESPOverlay EspOverlay { get; init; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface);

        _blacklistChecker = new BlacklistChecker(Log, GameInteropProvider);
        _hookHandler = new HookHandler(GameInteropProvider, Log, _blacklistChecker, Configuration);
        _hookHandler.Initialize();

        ConfigWindow = new ConfigWindow(this, _blacklistChecker, ObjectTable, ClientState);
        EspOverlay = new ESPOverlay(_blacklistChecker, ObjectTable, GameGui, TargetManager, Configuration, ClientState);
        
        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(EspOverlay);

        EspOverlay.IsOpen = true;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the Smart Block Checker config window"
        });

        CommandManager.AddHandler(SmartBlockCommand, new CommandInfo(OnSmartBlockCommand)
        {
            HelpMessage = "Instantly blacklists your current target without opening any menus."
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        
        WindowSystem.RemoveAllWindows();
        ConfigWindow.Dispose();
        EspOverlay.Dispose();

        _hookHandler.Dispose();
        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(SmartBlockCommand);
    }

    private void OnCommand(string command, string args)
    {
        ToggleConfigUi();
    }
    
    private unsafe void OnSmartBlockCommand(string command, string args)
    {
        var target = TargetManager.Target;
        if (target == null || target.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player)
        {
            ChatGui.PrintError("[SmartBlock] You must have a player targeted to use /smartblock.");
            return;
        }

        var character = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)target.Address;
        if (character == null) return;

        ulong contentId = character->ContentId;
        ulong accountId = character->AccountId;

        if (contentId == 0 && accountId == 0)
        {
            ChatGui.PrintError("[SmartBlock] Target has no valid ID to block.");
            return;
        }

        string name = target.Name.TextValue;

        if (_blacklistChecker.IsBlocked(contentId, accountId))
        {
            ChatGui.PrintError($"[SmartBlock] {name} is already blocked!");
            return;
        }

        bool success = _blacklistChecker.SmartBlockViaChat(name);
        if (success)
        {
            ChatGui.Print($"[SmartBlock] Successfully added {name} to the blacklist!");
        }
        else
        {
            ChatGui.PrintError($"[SmartBlock] Failed to add {name} to the blacklist. Check Dalamud log.");
        }
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
}
