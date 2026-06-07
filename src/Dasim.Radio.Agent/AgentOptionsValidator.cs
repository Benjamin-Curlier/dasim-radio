using Dasim.Radio.Messaging.KeyValue;
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

        // The presence key is rewritten each beat under a fixed TTL. If it isn't refreshed at least twice
        // per TTL window, a single late or dropped beat lets a live post expire and flicker offline — so
        // the interval must be at most half the presence TTL.
        TimeSpan maxInterval = ControlPlaneTtls.Presence / 2;
        if (options.HeartbeatInterval > maxInterval)
        {
            return ValidateOptionsResult.Fail(
                $"Agent:HeartbeatInterval ({options.HeartbeatInterval}) must be at most half the presence " +
                $"TTL ({ControlPlaneTtls.Presence}), i.e. <= {maxInterval}, so a live post never expires between beats.");
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
