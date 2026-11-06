using System.Diagnostics;
using System.Text;

namespace DIPlaybox.Core;

public static class ExternalLuaDecompiler
{
    public static async Task<string> RunAsync(byte[] bytes, string jar)
    {
        if (!File.Exists(jar) || !string.Equals(Path.GetExtension(jar), ".jar", StringComparison.OrdinalIgnoreCase)) throw new FileNotFoundException("Select your trusted unluac.jar first.", jar);
        string folder = Path.Combine(Path.GetTempPath(), "DIPlaybox-Lua-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string input = Path.Combine(folder, "input.luac");
        try
        {
            await File.WriteAllBytesAsync(input, bytes);
            var info = new ProcessStartInfo("java") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add("-jar"); info.ArgumentList.Add(Path.GetFullPath(jar)); info.ArgumentList.Add(input);
            using var process = Process.Start(info) ?? throw new IOException("Could not start Java.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try
            {
                Task<string> output = ReadBounded(process.StandardOutput, timeout.Token), error = ReadBounded(process.StandardError, timeout.Token);
                await Task.WhenAll(output, error, process.WaitForExitAsync(timeout.Token));
                if (process.ExitCode != 0) throw new InvalidDataException("unluac failed: " + error.Result[..Math.Min(error.Result.Length, 2000)]);
                if (string.IsNullOrWhiteSpace(output.Result)) throw new InvalidDataException("unluac returned no source text.");
                return output.Result;
            }
            catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        }
        finally
        {
            // Delete only the known input in the unique directory created above.
            if (File.Exists(input)) File.Delete(input);
            Directory.Delete(folder);
        }
    }
    private static async Task<string> ReadBounded(StreamReader reader, CancellationToken token)
    {
        var result = new StringBuilder(); var buffer = new char[4096]; int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), token)) > 0)
        {
            if (result.Length + read > 8 * 1024 * 1024) throw new InvalidDataException("Decompiler output exceeded 8 MiB.");
            result.Append(buffer, 0, read);
        }
        return result.ToString();
    }
}
