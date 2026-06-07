namespace Dasim.Radio.Messaging;

/// <summary>
/// Connection + transport-security configuration for a host's NATS connection, bound from the
/// <c>Nats</c> section. Security is opt-in: with no <see cref="CredsFile"/> and TLS disabled this is the
/// current anonymous, plaintext behaviour. Supplying a credentials file and/or enabling TLS lets a host
/// authenticate and encrypt without any code change — the first slice of transport security
/// (<see href="https://github.com/Benjamin-Curlier/dasim-radio/issues/11">#11</see>); per-client subject
/// permissions are enforced server-side once every host presents an identity.
/// </summary>
public sealed class RadioNatsOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Nats";

    /// <summary>The NATS server URL (or comma-separated cluster URLs).</summary>
    public string Url { get; set; } = "nats://srv_brk:4222";

    /// <summary>
    /// Path to a NATS <c>.creds</c> file (the standard NKey/JWT credentials artifact) the host
    /// authenticates with. <c>null</c>/empty = connect anonymously (current behaviour).
    /// </summary>
    public string? CredsFile { get; set; }

    /// <summary>Transport TLS settings. Disabled by default.</summary>
    public NatsTlsOptions Tls { get; set; } = new();
}

/// <summary>TLS settings for the NATS connection. <see cref="Enabled"/> is off by default (plaintext).</summary>
public sealed class NatsTlsOptions
{
    /// <summary>When <c>true</c>, the connection requires TLS.</summary>
    public bool Enabled { get; set; }

    /// <summary>PEM trust root for validating the server certificate (a private CA on a LAN).</summary>
    public string? CaFile { get; set; }

    /// <summary>Optional client certificate for mutual TLS (paired with <see cref="KeyFile"/>).</summary>
    public string? CertFile { get; set; }

    /// <summary>Private key for <see cref="CertFile"/>.</summary>
    public string? KeyFile { get; set; }

    /// <summary>
    /// Lab-only escape hatch: skip server-certificate validation. Never enable outside an isolated
    /// test network — it defeats the point of TLS.
    /// </summary>
    public bool InsecureSkipVerify { get; set; }
}
