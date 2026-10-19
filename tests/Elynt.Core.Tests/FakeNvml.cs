using Elynt.Core.Tuning;

namespace Elynt.Core.Tests;

/// <summary>
/// An in-memory NVML with GeForce-like partial support: each card can refuse any control, either outright (the
/// getter says not supported) or only when asked to change it (the getter works but the setter is refused).
/// Like the real driver, nothing it holds survives <see cref="Reboot"/>.
/// </summary>
public sealed class FakeNvml : INvml
{
    public const string Uuid = "GPU-11111111-2222-3333-4444-555555555555";

    private readonly List<Card> _cards = [];

    public string? UnavailableReason { get; set; }

    public int Writes { get; private set; }

    /// <summary>The next setter call fails with this NVML code.</summary>
    public int? FailNextSetWith { get; set; }

    public Card Add(string name = "NVIDIA GeForce RTX 4070", string uuid = Uuid)
    {
        var card = new Card(_cards.Count, name, uuid);
        _cards.Add(card);
        return card;
    }

    public Card this[int index] => _cards[index];

    /// <summary>The PC restarted or the driver reloaded: every NVML setting is back on defaults.</summary>
    public void Reboot()
    {
        foreach (var card in _cards)
        {
            card.PowerMilliwatts = card.DefaultPowerMilliwatts;
            card.CoreOffset = 0;
            card.MemoryOffset = 0;
            card.Lock = null;
        }
    }

    public IReadOnlyList<NvmlCard> Cards() => UnavailableReason is null
        ? _cards.Select(c => new NvmlCard(c.Index, c.Name, c.Uuid)).ToList()
        : throw new InvalidOperationException("NVML isn't loaded.");

    public NvmlPowerLimits? PowerLimits(int card)
    {
        var c = _cards[card];
        return c.Power == Support.None ? null : new NvmlPowerLimits(100_000, 250_000, c.DefaultPowerMilliwatts, c.PowerMilliwatts);
    }

    public void SetPowerLimit(int card, int milliwatts)
    {
        var c = _cards[card];
        Setter(c.Power);
        if (milliwatts is < 100_000 or > 250_000)
            throw new NvmlException(NvmlException.InvalidArgument, "Invalid argument");
        c.PowerMilliwatts = milliwatts;
    }

    public NvmlRange? ClockOffset(int card, NvmlClock clock)
    {
        var c = _cards[card];
        if (c.Offsets == Support.None)
            return null;
        return clock == NvmlClock.Graphics ? new NvmlRange(-500, 1000, c.CoreOffset) : new NvmlRange(-1000, 3000, c.MemoryOffset);
    }

    public void SetClockOffset(int card, NvmlClock clock, int mhz)
    {
        var c = _cards[card];
        Setter(c.Offsets);
        if (clock == NvmlClock.Graphics)
            c.CoreOffset = mhz;
        else
            c.MemoryOffset = mhz;
    }

    public (int Min, int Max)? SupportedGraphicsClocks(int card) =>
        _cards[card].Locking == Support.None ? null : (210, 3105);

    public void LockGraphicsClocks(int card, int minMHz, int maxMHz)
    {
        var c = _cards[card];
        Setter(c.Locking);
        if (minMHz > maxMHz)
            throw new NvmlException(NvmlException.InvalidArgument, "Invalid argument");
        c.Lock = (minMHz, maxMHz);
    }

    public void ResetGraphicsClocks(int card)
    {
        var c = _cards[card];
        Setter(c.Locking);
        c.Lock = null;
    }

    public NvmlReadings Readings(int card) => _cards[card].Readings;

    private void Setter(Support support)
    {
        if (FailNextSetWith is { } code)
        {
            FailNextSetWith = null;
            throw new NvmlException(code, "Unknown error");
        }
        if (support != Support.Full)
            throw new NvmlException(NvmlException.NotSupported, "Not supported");
        Writes++;
    }

    public enum Support
    {
        Full,
        ReadOnly,
        None,
    }

    public sealed class Card(int index, string name, string uuid)
    {
        public int Index { get; } = index;
        public string Name { get; } = name;
        public string Uuid { get; } = uuid;

        public Support Power { get; set; }
        public Support Offsets { get; set; }
        public Support Locking { get; set; }

        public int DefaultPowerMilliwatts { get; set; } = 200_000;
        public int PowerMilliwatts { get; set; } = 200_000;
        public int CoreOffset { get; set; }
        public int MemoryOffset { get; set; }
        public (int Min, int Max)? Lock { get; set; }

        public NvmlReadings Readings { get; set; } = new();

        public bool AtDefaults => PowerMilliwatts == DefaultPowerMilliwatts && CoreOffset == 0 && MemoryOffset == 0 && Lock is null;
    }
}
