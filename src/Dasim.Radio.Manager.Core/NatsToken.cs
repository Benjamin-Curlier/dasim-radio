namespace Dasim.Radio.Manager.Core;

/// <summary>
/// Validation for values that become single NATS subject tokens (ids, net ids). A bad token would
/// bind config/command traffic to the wrong subject, so the manager rejects it before it is stored or sent.
/// </summary>
internal static class NatsToken
{
    private static readonly char[] Illegal = ['.', '*', '>'];

    public static bool IsSingleToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (char c in value)
        {
            if (char.IsWhiteSpace(c) || Array.IndexOf(Illegal, c) >= 0)
            {
                return false;
            }
        }

        return true;
    }

    public static void EnsureSingleToken(string? value, string field)
    {
        if (!IsSingleToken(value))
        {
            throw new ArgumentException(
                $"'{field}' must be a single NATS token (no '.', '*', '>' or whitespace).", field);
        }
    }
}
