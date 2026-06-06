using Microsoft.Extensions.Options;

namespace Dasim.Radio.Agent;

/// <summary>
/// Fail-fast validation of <see cref="AgentOptions"/>. A bad <see cref="AgentOptions.HostId"/> would
/// otherwise silently bind the command service to a malformed or over-broad subject, so it is rejected
/// at startup rather than served.
/// </summary>
public sealed class AgentOptionsValidator : IValidateOptions<AgentOptions>
{
    // Characters that are illegal inside a single NATS subject token.
    private static readonly char[] IllegalTokenChars = ['.', '*', '>'];

    public ValidateOptionsResult Validate(string? name, AgentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.HostId))
        {
            return ValidateOptionsResult.Fail("Agent:HostId must be set.");
        }

        if (!IsSingleNatsToken(options.HostId))
        {
            return ValidateOptionsResult.Fail(
                $"Agent:HostId '{options.HostId}' must be a single NATS token (no '.', '*', '>' or whitespace).");
        }

        if (options.HeartbeatInterval <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail("Agent:HeartbeatInterval must be positive.");
        }

        return ValidateOptionsResult.Success;
    }

    private static bool IsSingleNatsToken(string value)
    {
        foreach (char c in value)
        {
            if (char.IsWhiteSpace(c) || Array.IndexOf(IllegalTokenChars, c) >= 0)
            {
                return false;
            }
        }

        return true;
    }
}
