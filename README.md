# SmartBlockChecker

SmartBlockChecker is a Dalamud plugin for FFXIV that helps you manage your blacklist more effectively and avoid wasting GCDs on people you've blocked.

### What it does:
*   **Prevent Wasted GCDs:** Hooks the game's action usage to prevent you from casting spells or using abilities on players who are on your blacklist.
*   **ESP Overlay:** Draws visual indicators (ESP circles) on blocked players in the world so you can easily identify them.
*   **Smart Blacklisting:** Allows you to instantly blacklist your current target with a simple command, bypassing multiple menus.
*   **In-game Integration:** Reads the native FFXIV blacklist directly using memory offsets for 100% accuracy.

### Commands:
*   `/blockchecker` - Opens the main configuration window.
*   `/smartblock` - Instantly blacklists your current target (must be a player).

### How to install:
1.  Ensure you have [XIVLauncher](https://goatcorp.github.io/) installed.
2.  Open the plugin installer in-game using `/xlplugins`.
3.  Go to `Settings` > `Experimental`.
4.  Add the following URL to your `Custom Plugin Repositories`:
    `https://cdn.jsdelivr.net/gh/InTheWind222/SmartBlockCheccker@main/pluginmaster.json`
5.  Search for `SmartBlockChecker` and click Install.

*Note: This is currently a standalone plugin. If you are building from source, follow the standard Dalamud plugin build process (Visual Studio 2022, .NET 8).*
