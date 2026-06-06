# Spike — global push-to-talk under Wayland vs X11 (issue #12)

> Decision record. Resolves the open risk flagged in
> [phase2-kickoff.md](phase2-kickoff.md) before the Avalonia client (issue #9) is built.

## Why this matters

Push-to-talk on a radio is **press-and-hold while looking elsewhere**: the operator is on a map,
a chat window, or another app, and presses PTT to transmit. So the PTT hotkey must be **global**
(captured while the client is unfocused) and must give a clean **key-down *and* key-up** (release
ends transmission — as load-bearing as the press). That requirement is what makes Linux hard.

## The constraint (verified June 2026)

- **SharpHook (the obvious hotkey library) wraps libuiohook, which is X11-only on Linux.** There is
  no Wayland backend (libuiohook Wayland support is an open, unstarted roadmap item). Worse, under a
  Wayland session libuiohook still attaches via **XWayland and runs without error**, but receives
  **no global events** — it **fails silently**, not loudly. So any SharpHook-on-Linux path *must* be
  guarded by an explicit session-type check, or PTT just quietly never fires on Wayland.
- **Wayland blocks unprivileged global key-grabs by design** (security): a normal client cannot read
  input destined for other surfaces, and there is no `XGrabKey` equivalent. An X11 app under
  XWayland likewise cannot grab input from native Wayland surfaces.
- **`evdev`** (reading `/dev/input/event*`) bypasses the display server entirely, so it works on
  **both X11 and Wayland**, on **every** compositor (GNOME, KDE, Sway, Hyprland), and delivers clean
  independent key-down/up — exactly PTT semantics. Cost: the process must read the input devices
  (`input`-group membership or a udev rule / `setcap`), and it sees **all** keyboards, so device
  enumeration + hotplug (watch udev/`/dev/input/by-id`, filter `EV_KEY`, re-enumerate on add/remove)
  is required. Reading does not *consume* the event, so the key still reaches the focused app.
- The desktop-native Wayland path is the XDG portal `org.freedesktop.portal.GlobalShortcuts`. It
  needs **no** `input`-group and **no** Xorg, and **does** support press-and-hold (`Activated` /
  `Deactivated` signals). Caveats: the backend shipped in **GNOME 48+** and **KDE Plasma 6+**; the
  user binds the key in the OS settings UI (the app registers an *action*, not a key); SharpHook
  doesn't wrap it (custom D-Bus, e.g. Tmds.DBus); and Mutter's implementation has had latency/dropped
  -event quirks (KDE's is the solid one).

Windows is unaffected — SharpHook's global hook works natively there.

## Options

| Option | Global PTT on X11 | Global PTT on Wayland | Cost / caveats |
|---|---|---|---|
| **A. SharpHook (libuiohook)** | ✅ | ❌ (silent no-op via XWayland) | Zero code beyond wiring; but Linux-Wayland is a dead end, and X11 sessions are being removed (GNOME 49 disables X11 by default). |
| **B. evdev reader** | ✅ | ✅ | One mechanism for all Linux + all compositors; clean down/up. Needs `input`-group / udev rule; reads all keyboards; device enumeration + hotplug code. |
| **C. GlobalShortcuts portal** | ✅ | ✅ (background) | No special permission, no Xorg, hold-capable. GNOME 48+/KDE 6+ only; key bound via OS settings, not in-app; custom D-Bus; Mutter latency caveat. |
| **D. Compositor shortcut → IPC** | ✅ | ✅ | Register a binding in the compositor (KDE/GNOME custom shortcut, Sway/Hyprland config) that runs a tiny CLI toggling PTT over IPC. No special permission, but most compositor bindings fire on key-**down** only → suits *toggle* PTT, not hold. |
| **E. Focused-only** | n/a | n/a | ❌ Unacceptable for radio — the operator won't keep the window focused. |

## Decision (v1)

Route PTT behind an **`IPushToTalkHotkey` seam** in the client (mirroring the `Dasim.Radio.Audio`
codec seam), with **one provider per OS** — not per Linux session type:

- **Windows → SharpHook global hook** (the well-supported native path).
- **Linux → `evdev` provider** (Option B). It is the *only* mechanism that covers X11 **and**
  Wayland with a single implementation, so it **eliminates** session-type branching and the silent
  XWayland failure mode entirely. For a radio that must keep working as distros go Wayland-only, this
  is the right v1 foundation — and the "operator-controlled LAN" nature of a post is exactly what
  makes its one real cost (granting input access at provision time) a non-issue we control.
- **On-screen / focused PTT button → universal fallback** when input access isn't granted (or the
  user is on a locked-down box), so the client is always usable.

This reverses the initial instinct to "require Xorg + SharpHook on Linux": that optimizes for zero
code now at the cost of a v1 whose global PTT dies on the session type the ecosystem is removing.
Requiring Xorg is therefore **demoted to a documented fallback**, not the plan.

## Implications for the client (issue #9)

- Introduce `IPushToTalkHotkey`; the UI depends only on the seam, never on a concrete hook library.
  (Avalonia's built-in `HotKey`/`KeyGesture` is **focused-only on every platform**, and Avalonia's
  Wayland backend is still maturing — so global PTT must live entirely outside Avalonia's input
  system.)
- Ship in #9: the **Windows SharpHook** provider, the **Linux evdev** provider (udev/`by-id`
  enumeration, `EV_KEY` filter, hotplug re-enumeration, configurable PTT key), and the **on-screen
  PTT** fallback. Surface the active PTT mode (and any "input access not granted" warning) in the
  UI/logs.
- Document the provisioning step (add the post's user to `input`, or ship a udev rule) in the deploy
  notes.

## Follow-ups (post-v1, separate issues)

- **GlobalShortcuts-portal provider** (Option C) — a **permission-free** path on GNOME 48+/KDE 6+;
  the natural fast-follow that lets locked-down Wayland boxes get global PTT without `input`-group.
- **SharpHook-on-X11** could be added behind the seam if an evdev-restricted X11 post ever needs a
  no-`input`-group option — low priority given the portal covers the permission-free case.

## Sources

- SharpHook / libuiohook (X11-only): <https://github.com/TolikPylypchuk/SharpHook>
- GNOME 48 global-shortcuts portal backend: <https://release.gnome.org/48/developers/>
- GlobalShortcuts portal interface (hold via Activated/Deactivated): <https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.GlobalShortcuts.html>
- XWayland input isolation: <https://wayland.freedesktop.org/docs/book/Xwayland.html>
- evdev global-hotkey reference (enumeration + hotplug): <https://github.com/DraconDev/obs-wayland-hotkey>
