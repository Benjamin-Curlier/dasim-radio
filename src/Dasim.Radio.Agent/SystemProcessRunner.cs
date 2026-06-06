using System.Diagnostics;

namespace Dasim.Radio.Agent;

/// <summary>
/// The real <see cref="IProcessRunner"/> over <see cref="Process"/>. Deliberately thin (no logic) —
/// every lifecycle decision lives in <see cref="ProcessClientController"/>, which is what the tests
/// exercise; this wrapper just maps to the OS.
/// </summary>
public sealed class SystemProcessRunner : IProcessRunner
{
    public IProcessHandle Start(string executablePath, string configId)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(configId);

        Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start client process '{executablePath}'.");

        return new SystemProcessHandle(process);
    }

    private sealed class SystemProcessHandle(Process process) : IProcessHandle
    {
        // Valid only because the handle is created after a successful Process.Start; HasExited would
        // throw if read on a never-started process.
        public bool HasExited => process.HasExited;

        public void Kill() => process.Kill(entireProcessTree: true);

        public void Dispose() => process.Dispose();
    }
}
