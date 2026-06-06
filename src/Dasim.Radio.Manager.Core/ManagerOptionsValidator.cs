using Microsoft.Extensions.Options;

namespace Dasim.Radio.Manager.Core;

/// <summary>Fail-fast validation of <see cref="ManagerOptions"/>.</summary>
public sealed class ManagerOptionsValidator : IValidateOptions<ManagerOptions>
{
    public ValidateOptionsResult Validate(string? name, ManagerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.PresenceStaleAfter <= TimeSpan.Zero
            ? ValidateOptionsResult.Fail("Manager:PresenceStaleAfter must be positive.")
            : ValidateOptionsResult.Success;
    }
}
