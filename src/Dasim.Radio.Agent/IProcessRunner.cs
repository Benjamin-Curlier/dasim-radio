namespace Dasim.Radio.Agent;

/// <summary>
/// Starts client processes. The seam exists so <see cref="ProcessClientController"/> — which carries
/// all the lifecycle logic — can be unit-tested without spawning a real OS process.
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Launches <paramref name="executablePath"/>, passing <paramref name="configId"/> to the child so
    /// it knows which configuration to load. Returns a handle to the running process.
    /// </summary>
    IProcessHandle Start(string executablePath, string configId);
}

/// <summary>A handle to a launched client process. Disposing it releases the underlying OS handle.</summary>
public interface IProcessHandle : IDisposable
{
    /// <summary>Whether the process has exited (on its own or after a <see cref="Kill"/>).</summary>
    bool HasExited { get; }

    /// <summary>Terminates the process immediately.</summary>
    void Kill();
}
