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

    /// <summary>
    /// Connection options from <paramref name="options"/>, layering authentication (a NATS
    /// <c>.creds</c> file) and TLS on top of <see cref="ForUrl"/> when configured. With no credentials
    /// and TLS disabled the result is identical to <see cref="ForUrl"/> (anonymous, plaintext) — security
    /// is opt-in, so this is safe to call unconditionally.
    /// </summary>
    public static NatsOpts Build(RadioNatsOptions options, string name = DefaultName)
    {
        ArgumentNullException.ThrowIfNull(options);

        NatsOpts opts = ForUrl(options.Url, name);

        if (!string.IsNullOrWhiteSpace(options.CredsFile))
        {
            opts = opts with { AuthOpts = opts.AuthOpts with { CredsFile = options.CredsFile } };
        }

        NatsTlsOptions tls = options.Tls;
        if (tls.Enabled || tls.CaFile is not null || tls.CertFile is not null)
        {
            opts = opts with
            {
                TlsOpts = opts.TlsOpts with
                {
                    Mode = tls.Enabled ? TlsMode.Require : opts.TlsOpts.Mode,
                    CaFile = tls.CaFile ?? opts.TlsOpts.CaFile,
                    CertFile = tls.CertFile ?? opts.TlsOpts.CertFile,
                    KeyFile = tls.KeyFile ?? opts.TlsOpts.KeyFile,
                    InsecureSkipVerify = tls.InsecureSkipVerify,
                },
            };
        }

        return opts;
    }
}
