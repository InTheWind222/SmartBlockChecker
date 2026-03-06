# Smart Block Checker

Smart Block Checker is a Dalamud plugin for FFXIV focused on one thing: reducing friction around your in-game blacklist.

It prevents targeted actions from firing on blocked players, highlights blocked players in the world, and gives you a quick command for blacklisting your current target without digging through UI menus.

## Features

- Prevents targeted actions from being used on blocked players
- Draws ESP markers over blocked players in the world
- Shows a clear warning banner when your current target is blocked
- Adds a one-command quick blacklist flow with `/smartblock`
- Reads blacklist state directly from the game client for accurate matches

## Commands

- `/blockchecker` opens the configuration window
- `/smartblock` blacklists your current target

## Installation

1. Open `/xlplugins` in XIVLauncher.
2. Go to `Settings` > `Experimental`.
3. Add this custom repository URL:

```text
https://cdn.jsdelivr.net/gh/InTheWind222/SmartBlockChecker@main/pluginmaster.json
```

4. Search for `Smart Block Checker` in the installer and install it.

## Build

- Visual Studio 2022
- .NET 8 SDK
- XIVLauncher with Dalamud development files available locally

## Notes

- The download count shown by Dalamud comes from repository metadata in `pluginmaster.json`.
- The plugin uses the game's own blacklist data. It does not maintain a separate blacklist database.
