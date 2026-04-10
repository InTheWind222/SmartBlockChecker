using System;
using System.Collections.Generic;
using System.Text;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace SmartBlockChecker;

internal sealed class BlacklistEntry
{
    public ulong Identifier { get; init; }

    public string Name { get; init; } = string.Empty;

    public string NormalizedName { get; init; } = string.Empty;
}

internal unsafe sealed class BlacklistService
{
    private delegate void ProcessChatBoxDelegate(nint uiModule, nint message, nint unused, byte a4);
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(500);

#pragma warning disable CS0649 // Set by Dalamud signature injection at runtime.
    [Signature("48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC 20 48 8B F2 48 8B F9 45")]
    private ProcessChatBoxDelegate? _processChatBox;
#pragma warning restore CS0649

    private readonly IPluginLog _log;
    private readonly object _cacheLock = new();
    private readonly HashSet<ulong> _cachedIdentifiers = new();
    private readonly HashSet<string> _cachedNames = new(StringComparer.OrdinalIgnoreCase);
    private List<BlacklistEntry> _cachedEntries = new();
    private DateTime _lastRefreshUtc = DateTime.MinValue;
    private bool _hasSuccessfulScan;
    private bool _isDataUnavailable;

    public string DiagnosticInfo { get; private set; } = "Blacklist has not been scanned yet.";
    public bool HasUsableData => _hasSuccessfulScan;
    public bool IsDataUnavailable => _isDataUnavailable;

    private const int EntryArrayOffset = 0xF0;
    private const int BlockedCountOffset = 0x19F0;
    private const int EntrySize = 0x20;
    private const int MaxEntries = 200;

    private const int NamePointerOffset = 0x00;
    private const int IdentifierOffset = 0x10;

    public BlacklistService(IPluginLog log, IGameInteropProvider interopProvider)
    {
        _log = log;
        interopProvider.InitializeFromAttributes(this);
        _log.Information("Blacklist service initialized. ProcessChatBox signature found: {Found}", _processChatBox is not null);
        RefreshCache(forceRefresh: true);
    }

    public bool ExecuteBlacklistCommand()
    {
        if (_processChatBox is null)
        {
            _log.Error("ProcessChatBox signature was not found. Cannot execute /blacklist command.");
            return false;
        }

        try
        {
            var uiModule = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework.Instance()->UIModule;
            if (uiModule is null)
            {
                _log.Error("UIModule is null. Cannot execute /blacklist command.");
                return false;
            }

            const string command = "/blacklist add <t>";
            byte[] commandBytes = Encoding.UTF8.GetBytes(command + "\0");

            fixed (byte* commandPointer = commandBytes)
            {
                var utfCommand = new FFXIVClientStructs.FFXIV.Client.System.String.Utf8String();
                utfCommand.Ctor();
                try
                {
                    utfCommand.SetString(commandPointer);
                    _processChatBox((nint)uiModule, (nint)(&utfCommand), IntPtr.Zero, 0);
                }
                finally
                {
                    utfCommand.Dtor();
                }
            }

            _log.Information("Executed native blacklist command through ProcessChatBox.");
            RefreshCache(forceRefresh: true);
            return true;
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to execute the native /blacklist command.");
            return false;
        }
    }

    public bool IsBlocked(ulong contentId, ulong accountId)
    {
        return IsBlocked(contentId, accountId, null);
    }

    public bool IsBlocked(ulong contentId, ulong accountId, string? playerName)
    {
        if (contentId == 0 && accountId == 0 && string.IsNullOrWhiteSpace(playerName))
        {
            return false;
        }

        RefreshCache();

        string normalizedPlayerName = NormalizeName(playerName);
        lock (_cacheLock)
        {
            if (_cachedIdentifiers.Contains(contentId) || _cachedIdentifiers.Contains(accountId))
            {
                return true;
            }

            return !string.IsNullOrEmpty(normalizedPlayerName) && _cachedNames.Contains(normalizedPlayerName);
        }
    }

    public List<BlacklistEntry> GetEntries(bool forceRefresh = false)
    {
        RefreshCache(forceRefresh);
        lock (_cacheLock)
        {
            return new List<BlacklistEntry>(_cachedEntries);
        }
    }

    public bool RefreshCache(bool forceRefresh = false)
    {
        try
        {
            lock (_cacheLock)
            {
                if (!forceRefresh && DateTime.UtcNow - _lastRefreshUtc < RefreshInterval)
                {
                    return _hasSuccessfulScan;
                }
            }

            var proxy = InfoProxyBlacklist.Instance();
            if (proxy is null)
            {
                return HandleUnavailableSnapshot("Blacklist info proxy is not loaded yet.");
            }

            byte* proxyBase = (byte*)proxy;
            int blockedCount = *(int*)(proxyBase + BlockedCountOffset);
            if (blockedCount < 0 || blockedCount > MaxEntries)
            {
                return HandleUnavailableSnapshot($"Blacklist count is invalid: {blockedCount}.");
            }

            var entries = new List<BlacklistEntry>(blockedCount);
            var identifiers = new HashSet<ulong>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            byte* rawEntries = proxyBase + EntryArrayOffset;
            for (int index = 0; index < blockedCount; index++)
            {
                byte* entry = rawEntries + (index * EntrySize);
                ulong identifier = *(ulong*)(entry + IdentifierOffset);
                if (identifier == 0)
                {
                    continue;
                }

                string name = ReadCStringPointer(entry + NamePointerOffset);
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = $"ID:0x{identifier:X}";
                }

                string normalizedName = NormalizeName(name);
                entries.Add(new BlacklistEntry
                {
                    Identifier = identifier,
                    Name = name,
                    NormalizedName = normalizedName
                });
                identifiers.Add(identifier);
                if (!string.IsNullOrEmpty(normalizedName))
                {
                    names.Add(normalizedName);
                }
            }

            lock (_cacheLock)
            {
                _cachedEntries = entries;
                _cachedIdentifiers.Clear();
                _cachedIdentifiers.UnionWith(identifiers);
                _cachedNames.Clear();
                _cachedNames.UnionWith(names);
                _hasSuccessfulScan = true;
                _isDataUnavailable = false;
                _lastRefreshUtc = DateTime.UtcNow;
                DiagnosticInfo = $"Loaded {entries.Count} blacklist entries.";
            }

            return true;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed while reading blacklist entries.");
            return HandleUnavailableSnapshot($"Blacklist scan failed: {ex.GetType().Name}");
        }
    }

    private bool HandleUnavailableSnapshot(string message)
    {
        lock (_cacheLock)
        {
            _lastRefreshUtc = DateTime.UtcNow;
            _isDataUnavailable = true;
            DiagnosticInfo = _hasSuccessfulScan
                ? $"{message} Using last known blacklist snapshot."
                : message;
            return _hasSuccessfulScan;
        }
    }

    private static string ReadCStringPointer(byte* address)
    {
        try
        {
            byte* stringPointer = *(byte**)address;
            if (stringPointer is null || (ulong)stringPointer < 0x10000)
            {
                return string.Empty;
            }

            int length = 0;
            while (length < 64 && stringPointer[length] != 0)
            {
                length++;
            }

            return length == 0 ? string.Empty : Encoding.UTF8.GetString(stringPointer, length);
        }
        catch
        {
            return string.Empty;
        }
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
}
