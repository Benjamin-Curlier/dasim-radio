using Dasim.Radio.Messaging.Serialization;
using NATS.Client.Core;

namespace Dasim.Radio.Messaging;

/// <summary>Builds <see cref="NatsOpts"/> wired with the Dasim.Radio serializer registry.</summary>
public static class RadioNatsOpts
{
    /// <summary>Default client name reported to the NATS server.</summary>
    public const string DefaultName = "dasim-radio";

    /// <summary>
    /// Connection options for <paramref name="url"/> using the <see cref="RadioSerializerRegistry"/>.
    /// Callers may layer TLS/auth on top with a <c>with</c> expression as long as they keep the
    /// registry.
    /// </summary>
    public static NatsOpts ForUrl(string url, string name = DefaultName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        return NatsOpts.Default with
        {
            Url = url,
            Name = name,
            SerializerRegistry = RadioSerializerRegistry.Default,
        };
    }
}
