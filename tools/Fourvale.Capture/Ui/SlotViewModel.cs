using Fourvale.Adapter;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using Fourvale.Adapter.Colyseus;
using Fourvale.Adapter.Network;
using Fourvale.Capture.Capture;

namespace Fourvale.Capture.Ui;

public sealed class MessageTypeRow(string key, string glyph, string label) : Observable
{
    private int _count;

    public string Key { get; } = key;
    public string Glyph { get; } = glyph;
    public string Label { get; } = label;

    public int Count
    {
        get => _count;
        set => Set(ref _count, value);
    }
}

/// <summary>One account slot: its profile, whether its game view is open, and its capture.</summary>
public sealed class SlotViewModel : Observable
{
    private readonly int _messageListSize;
    private bool _isOpen;
    private bool _isActive;
    private bool _isReady;
    private string _gameStatus = "Closed";

    public SlotViewModel(int number, Redactor redactor, int messageListSize)
    {
        Number = number;
        ProfileName = $"slot{number}";
        Capture = new SlotCapture(ProfileName, redactor);
        _messageListSize = messageListSize;
    }

    public int Number { get; }
    public string ProfileName { get; }
    public string Name => $"Slot {Number}";
    public SlotCapture Capture { get; }
    public FourvaleSession Live { get; } = new();
    public ObservableCollection<MessageTypeRow> MessageTypes { get; } = [];

    public bool IsOpen
    {
        get => _isOpen;
        set => Set(ref _isOpen, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set => Set(ref _isActive, value);
    }

    public bool IsReady
    {
        get => _isReady;
        set => Set(ref _isReady, value);
    }

    public string GameStatus
    {
        get => _gameStatus;
        set => Set(ref _gameStatus, value);
    }

    public bool IsRecording => Capture.IsRecording;
    public int Received => Capture.Received;
    public int Sent => Capture.Sent;
    public int Http => Capture.Http;
    public int Redacted => Capture.Redacted;
    public int Dropped => Capture.Dropped;
    public int Undecoded => Capture.Undecoded;
    public string Elapsed => IsRecording ? Capture.Elapsed.ToString(@"hh\:mm\:ss") : "00:00:00";

    public string SavedSize => IsRecording ? $"{Capture.RecordCount:N0} records · {FormatBytes(Capture.BytesWritten)}" : "";

    public string FileText => IsRecording
        ? $"Saving to {Path.GetFileName(Capture.FilePath)}"
        : Capture.LastFilePath is { } last ? $"Last capture: {Path.GetFileName(last)}" : "Nothing is saved until you press Start.";

    /// <summary>Called once a second by the main view model.</summary>
    public void Refresh()
    {
        Raise(nameof(IsRecording));
        Raise(nameof(Received));
        Raise(nameof(Sent));
        Raise(nameof(Http));
        Raise(nameof(Redacted));
        Raise(nameof(Dropped));
        Raise(nameof(Undecoded));
        Raise(nameof(Elapsed));
        Raise(nameof(SavedSize));
        Raise(nameof(FileText));

        var top = Capture.Tally
            .OrderByDescending(p => p.Value.Count)
            .ThenBy(p => p.Key, StringComparer.Ordinal)
            .Take(_messageListSize)
            .ToList();

        if (top.Select(p => p.Key).SequenceEqual(MessageTypes.Select(r => r.Key)))
        {
            for (var i = 0; i < top.Count; i++)
            {
                MessageTypes[i].Count = top[i].Value.Count;
            }

            return;
        }

        MessageTypes.Clear();
        foreach (var (key, value) in top)
        {
            MessageTypes.Add(new MessageTypeRow(key, value.Glyph, value.Label) { Count = value.Count });
        }
    }

    // ----- Decoded live state (debug view). Missing or closed-room values show UNKNOWN (spec §6.1). -----

    private const string Unknown = "UNKNOWN";
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private string _roomText = Unknown, _classText = Unknown, _levelText = Unknown, _hpText = Unknown, _spText = Unknown;
    private string _meterText = Unknown, _battleText = Unknown, _decodeText = "", _stateJson = "";
    private double _hpFraction, _spFraction, _meterFraction;
    private bool _hasHpBar, _hasSpBar, _hasMeter, _isReadyToAct;

    public string RoomText { get => _roomText; private set => Set(ref _roomText, value); }
    public string ClassText { get => _classText; private set => Set(ref _classText, value); }
    public string LevelText { get => _levelText; private set => Set(ref _levelText, value); }
    public string HpText { get => _hpText; private set => Set(ref _hpText, value); }
    public string SpText { get => _spText; private set => Set(ref _spText, value); }
    public string MeterText { get => _meterText; private set => Set(ref _meterText, value); }
    public string BattleText { get => _battleText; private set => Set(ref _battleText, value); }
    public string DecodeText { get => _decodeText; private set => Set(ref _decodeText, value); }
    public string StateJson { get => _stateJson; private set => Set(ref _stateJson, value); }
    public double HpFraction { get => _hpFraction; private set => Set(ref _hpFraction, value); }
    public double SpFraction { get => _spFraction; private set => Set(ref _spFraction, value); }
    public double MeterFraction { get => _meterFraction; private set => Set(ref _meterFraction, value); }
    public bool HasHpBar { get => _hasHpBar; private set => Set(ref _hasHpBar, value); }
    public bool HasSpBar { get => _hasSpBar; private set => Set(ref _hasSpBar, value); }
    public bool HasMeter { get => _hasMeter; private set => Set(ref _hasMeter, value); }
    public bool IsReadyToAct { get => _isReadyToAct; private set => Set(ref _isReadyToAct, value); }

    /// <summary>Reads this slot's own entry from the current room. Called several times a second for the active slot.</summary>
    public void RefreshLive(bool includeJson)
    {
        var room = Live.Rooms.Current;
        var own = room?.OwnEntry();
        var map = own?.Get("mapId") as string ?? room?.State?.Root.Get("mapId") as string;

        RoomText = room is null ? Unknown : map is null ? room.Name : $"{room.Name} · {map}";
        ClassText = own?.Get("classId") is string classId ? $"{Pretty(classId)}" : Unknown;
        LevelText = own?.Get("level") is long level ? level.ToString("N0", CultureInfo.CurrentCulture) : Unknown;

        var inBattle = room?.Name == "battle" || own?.Get("inBattle") is true;
        BattleText = room is null ? Unknown : inBattle ? "Yes" : "No";

        if (own?.Get("maxHp") is long maxHp && own.Get("hp") is long hp)
        {
            HpText = $"{hp:N0} / {maxHp:N0}";
            HpFraction = maxHp > 0 ? Math.Clamp(hp / (double)maxHp, 0, 1) : 0;
            HasHpBar = true;
        }
        else
        {
            HpText = Live.LastHpSync is { } sync && room is not null ? $"{sync.Hp:N0}  (sent on room join)" : Unknown;
            HasHpBar = false;
        }

        if (own?.Get("maxSp") is long maxSp && own.Get("sp") is long sp)
        {
            SpText = $"{sp:N0} / {maxSp:N0}";
            SpFraction = maxSp > 0 ? Math.Clamp(sp / (double)maxSp, 0, 1) : 0;
            HasSpBar = true;
        }
        else
        {
            SpText = Live.LastHpSync is { } sync && room is not null ? $"{sync.Sp:N0}  (sent on room join)" : Unknown;
            HasSpBar = false;
        }

        if (own?.Get("actionMeter") is { } meterValue and (long or double))
        {
            var meter = Math.Clamp(Convert.ToDouble(meterValue, CultureInfo.InvariantCulture), 0, 1);
            MeterFraction = meter;
            HasMeter = true;
            IsReadyToAct = meter >= 1;
            MeterText = IsReadyToAct ? "READY" : $"{meter:P0}";
        }
        else
        {
            HasMeter = false;
            IsReadyToAct = false;
            MeterText = room is null ? Unknown : "not in battle";
        }

        DecodeText = room is null
            ? $"No room yet · {Live.Rooms.TotalErrors} decode errors"
            : $"{room.Patches:N0} updates in this room · {Live.Rooms.TotalErrors} decode errors";

        if (includeJson)
        {
            var json = room?.State?.ToJson().ToJsonString(PrettyJson) ?? "";
            StateJson = room is { OwnSessionId.Length: > 0 } ? json.Replace(room.OwnSessionId, "@me", StringComparison.Ordinal) : json;
        }
    }

    private static string Pretty(string id) =>
        string.Join(' ', id.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.0} KB",
        _ => $"{bytes / (1024.0 * 1024):0.0} MB",
    };
}
