namespace Dasim.Radio.Core;

/// <summary>Identifies a participant (a person/post) on the radio network.</summary>
public readonly record struct ParticipantId
{
    public ParticipantId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Participant id must not be empty.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>Identifies a net (a communication group derived from the force tree).</summary>
public readonly record struct NetId
{
    public NetId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Net id must not be empty.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>
/// Transmission authority on a net. A strictly higher value wins the floor and pre-empts
/// a lower value (a chief cuts off a subordinate).
/// </summary>
public readonly record struct Priority(int Value) : IComparable<Priority>
{
    public int CompareTo(Priority other) => Value.CompareTo(other.Value);

    public static bool operator <(Priority left, Priority right) => left.Value < right.Value;

    public static bool operator >(Priority left, Priority right) => left.Value > right.Value;

    public static bool operator <=(Priority left, Priority right) => left.Value <= right.Value;

    public static bool operator >=(Priority left, Priority right) => left.Value >= right.Value;
}
