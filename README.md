# SmartBlockChecker

SmartBlockChecker is a Dalamud plugin for FFXIV that helps you manage your blacklist more effectively and avoid wasting GCDs on people you've blocked.

### What it does:
*   **Prevent Wasted GCDs:** Hooks the game's action usage to prevent you from casting spells or using abilities on players who are on your blacklist.
*   **ESP Overlay:** Draws visual indicators (ESP circles) on blocked players in the world so you can easily identify them.
*   **Smart Blacklisting:** Allows you to instantly blacklist your current target with a simple command, bypassing multiple menus.
*   **In-game Integration:** Reads the native FFXIV blacklist directly using memory offsets for 100% accuracy.
*   **Active Install Counting:** Can report anonymous active installs to a telemetry endpoint once per day and show the last known active-user count in the config UI.

### Commands:
*   `/blockchecker` - Opens the main configuration window.
*   `/smartblock` - Instantly blacklists your current target (must be a player).

### How to install:
1.  Ensure you have [XIVLauncher](https://goatcorp.github.io/) installed.
2.  Open the plugin installer in-game using `/xlplugins`.
3.  Go to `Settings` > `Experimental`.
4.  Add the following URL to your `Custom Plugin Repositories`:
    `https://cdn.jsdelivr.net/gh/InTheWind222/SmartBlockChecker@main/pluginmaster.json`
5.  Search for `SmartBlockChecker` and click Install.

*Note: This is currently a standalone plugin. If you are building from source, follow the standard Dalamud plugin build process (Visual Studio 2022, .NET 8).*

### Active install telemetry setup
1.  Deploy the worker in `telemetry-worker/`.
2.  Copy the worker `/v1/stats` URL into the GitHub secret `TELEMETRY_STATS_URL`.
3.  Put the worker base URL into the plugin's `Telemetry Endpoint` setting.
4.  Push a new release so the workflow can mirror the active-install count into `pluginmaster.json`.

The plugin sends only an anonymous random install ID, plugin name, and plugin version. It does not send character names, account IDs, or chat content.
