using Microsoft.Extensions.Options;

namespace Dasim.Radio.Client;

/// <summary>
/// Fail-fast validation of <see cref="ClientOptions"/>: the ids that become NATS subject tokens must
/// be single tokens, or audio/floor traffic would bind to the wrong subjects.
/// </summary>
public sealed class ClientOptionsValidator : IValidateOptions<ClientOptions>
{
    private static readonly char[] IllegalTokenChars = ['.', '*', '>'];

    public ValidateOptionsResult Validate(string? name, ClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (Invalid(options.ClientId, out string clientError, nameof(ClientOptions.ClientId)))
        {
            return ValidateOptionsResult.Fail(clientError);
        }

        if (Invalid(options.ParticipantId, out string participantError, nameof(ClientOptions.ParticipantId)))
        {
            return ValidateOptionsResult.Fail(participantError);
        }

        if (Invalid(options.OwnNetId, out string netError, nameof(ClientOptions.OwnNetId)))
        {
            return ValidateOptionsResult.Fail(netError);
        }

        if (options.ParentNetId is not null && !IsSingleNatsToken(options.ParentNetId))
        {
            return ValidateOptionsResult.Fail("Client:ParentNetId must be a single NATS token when set.");
        }

        // The encoder tuning binds from config but is otherwise only validated lazily, inside the transmit
        // pump (a fire-and-forget Task): a bad value there faults the pump and silently stops ALL
        // transmission while the client still looks healthy. Validate it up front so misconfiguration
        // fails fast at ValidateOnStart instead.
        try
        {
            options.EncoderSettings.Validate();
        }
        catch (ArgumentException ex)
        {
            return ValidateOptionsResult.Fail($"Client:EncoderSettings is invalid: {ex.Message}");
        }

        return ValidateOptionsResult.Success;
    }

    private static bool Invalid(string value, out string error, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            error = $"Client:{field} must be set.";
            return true;
        }

        if (!IsSingleNatsToken(value))
        {
            error = $"Client:{field} '{value}' must be a single NATS token (no '.', '*', '>' or whitespace).";
            return true;
        }

        error = string.Empty;
        return false;
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
