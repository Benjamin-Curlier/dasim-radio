namespace Dasim.Radio.Client;

/// <summary>The desktop session type, which decides how global push-to-talk must be captured on Linux.</summary>
public enum SessionType
{
    /// <summary>X11 / Xorg — a global keyboard hook (SharpHook/libuiohook) works.</summary>
    X11,

    /// <summary>Wayland — unprivileged global key-grabs are blocked; use evdev (or the on-screen fallback).</summary>
    Wayland,

    /// <summary>Neither could be determined (e.g. a headless/tty session).</summary>
    Other,
}

/// <summary>
/// Detects the desktop <see cref="SessionType"/> from environment variables, per the decision in
/// <c>docs/wayland-ptt-spike.md</c>. The lookup is injected so it is deterministically testable.
/// </summary>
public static class SessionTypeDetector
{
    /// <summary>Detects the session type using the given environment-variable lookup.</summary>
    public static SessionType Detect(Func<string, string?> getEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        string? sessionType = getEnvironmentVariable("XDG_SESSION_TYPE");
        if (string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase))
        {
            return SessionType.Wayland;
        }

        if (string.Equals(sessionType, "x11", StringComparison.OrdinalIgnoreCase))
        {
            return SessionType.X11;
        }

        // XDG_SESSION_TYPE unset/unknown — fall back to the display sockets.
        if (!string.IsNullOrEmpty(getEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            return SessionType.Wayland;
        }

        return string.IsNullOrEmpty(getEnvironmentVariable("DISPLAY")) ? SessionType.Other : SessionType.X11;
    }

    /// <summary>Detects the session type from the process environment.</summary>
    public static SessionType Detect() => Detect(Environment.GetEnvironmentVariable);
}
