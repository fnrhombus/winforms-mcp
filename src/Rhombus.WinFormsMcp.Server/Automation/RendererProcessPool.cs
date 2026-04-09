using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Caching.Memory;

namespace Rhombus.WinFormsMcp.Server.Automation;

/// <summary>
/// Manages a pool of out-of-process RendererHost instances keyed by TFM category.
/// Each host is a long-lived process that accepts JSON render requests over stdin/stdout.
/// Processes are reused across calls and killed after an idle timeout.
/// </summary>
public sealed class RendererProcessPool : IDisposable {
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The three host TFMs we ship. Every target framework maps to one of these.
    /// </summary>
    private static readonly string[] HostTfms = { "net48", "netcoreapp3.1", "net8.0-windows" };

    private readonly IMemoryCache _cache;
    private readonly ConcurrentBag<string> _activeKeys = new();
    private readonly object _createLock = new();
    private readonly Lazy<string> _hostBasePath;
    private bool _disposed;

    /// <param name="cache">Memory cache instance for managing host entries.</param>
    /// <param name="hostBasePath">
    /// Directory containing the RendererHost build output (with subdirs per TFM).
    /// If null, auto-detected relative to this assembly on first use.
    /// </param>
    public RendererProcessPool(IMemoryCache cache, string? hostBasePath = null) {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _hostBasePath = hostBasePath != null
            ? new Lazy<string>(hostBasePath)
            : new Lazy<string>(DetectHostBasePath);
    }

    /// <summary>
    /// Render designer code using the appropriate out-of-process host for the given TFM.
    /// </summary>
    /// <param name="designerContent">Content of the .Designer.cs file.</param>
    /// <param name="companionContent">Content of the companion .cs file (optional).</param>
    /// <param name="extraAssemblyPaths">Extra assembly search paths (optional).</param>
    /// <param name="targetTfm">
    /// The TFM to render under, or "auto" to detect from csproj.
    /// When "auto", <paramref name="csprojPath"/> must be provided.
    /// </param>
    /// <param name="csprojPath">Path to the project's .csproj (used when targetTfm is "auto").</param>
    /// <returns>PNG bytes of the rendered form.</returns>
    public async Task<byte[]> RenderAsync(
        string designerContent,
        string? companionContent,
        string[]? extraAssemblyPaths,
        string targetTfm,
        string? csprojPath = null) {
        if (_disposed) throw new ObjectDisposedException(nameof(RendererProcessPool));

        var hostTfm = ResolveHostTfm(targetTfm, csprojPath);
        var entry = GetOrCreateEntry(hostTfm);

        return await entry.RenderAsync(designerContent, companionContent, extraAssemblyPaths);
    }

    /// <summary>
    /// Read the TFM env var. Returns the value, or "auto" if absent/empty.
    /// </summary>
    public static string GetConfiguredTfm() {
        var val = Environment.GetEnvironmentVariable("TFM");
        return string.IsNullOrWhiteSpace(val) ? "auto" : val.Trim();
    }

    /// <summary>
    /// Detect the target framework from a .csproj file's TargetFramework(s) element.
    /// Returns the first TFM found.
    /// </summary>
    public static string DetectTfmFromCsproj(string csprojPath) {
        var doc = XDocument.Load(csprojPath);

        // Helper: search for an element by local name, ignoring XML namespace.
        // SDK-style csproj has no namespace; old-style has xmlns="http://schemas.microsoft.com/developer/msbuild/2003".
        XElement? FindByLocalName(string localName) =>
            doc.Root?.Descendants().FirstOrDefault(e =>
                string.Equals(e.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase));

        // SDK-style: <TargetFramework> or <TargetFrameworks>
        var tfElem = FindByLocalName("TargetFramework") ?? FindByLocalName("TargetFrameworks");

        if (tfElem != null && !string.IsNullOrWhiteSpace(tfElem.Value)) {
            var raw = tfElem.Value.Split(';')[0].Trim();
            return raw;
        }

        // Old-style csproj: <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
        var tfvElem = FindByLocalName("TargetFrameworkVersion");
        if (tfvElem != null && !string.IsNullOrWhiteSpace(tfvElem.Value)) {
            // Convert "v4.7.2" → "net472" (strip 'v', remove dots)
            var ver = tfvElem.Value.Trim().TrimStart('v', 'V').Replace(".", "");
            return $"net{ver}";
        }

        throw new InvalidOperationException(
            $"No TargetFramework or TargetFrameworkVersion found in {csprojPath}. " +
            "You can bypass auto-detection by setting the TFM environment variable " +
            "(e.g. TFM=net48 or TFM=net8.0-windows).");
    }

    /// <summary>
    /// Map any target framework moniker to the closest host TFM we ship.
    /// </summary>
    public static string MapToHostTfm(string projectTfm) {
        var tfm = projectTfm.ToLowerInvariant().Trim();

        // .NET Framework 4.x → net48 host
        if (tfm.StartsWith("net4") && !tfm.Contains("."))
            return "net48";
        // net40, net45, net451, net452, net46, net461, net462, net47, net471, net472, net48, net481
        if (tfm.StartsWith("net") && tfm.Length <= 6 && !tfm.Contains(".") && !tfm.Contains("-")) {
            if (int.TryParse(tfm.Substring(3), out var ver) && ver >= 20 && ver < 500)
                return "net48";
        }

        // .NET Core 3.x → netcoreapp3.1 host
        if (tfm.StartsWith("netcoreapp3"))
            return "netcoreapp3.1";

        // .NET Core 1.x/2.x don't support WinForms — shouldn't happen, but fallback to net8
        if (tfm.StartsWith("netcoreapp"))
            return "net8.0-windows";

        // .NET 5+ (net5.0-windows, net6.0-windows, net7.0-windows, net8.0-windows, net9.0-windows, etc.)
        if (tfm.StartsWith("net") && tfm.Contains("."))
            return "net8.0-windows";

        // Fallback — best guess is the newest host
        return "net8.0-windows";
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;

        // Removing from cache triggers eviction callbacks which dispose HostEntries
        foreach (var key in _activeKeys) {
            _cache.Remove(key);
        }
    }

    private HostEntry GetOrCreateEntry(string hostTfm) {
        var key = $"RendererHost:{hostTfm}";
        if (_cache.TryGetValue<HostEntry>(key, out var existing))
            return existing!;

        lock (_createLock) {
            if (_cache.TryGetValue<HostEntry>(key, out existing))
                return existing!;

            var entry = new HostEntry(hostTfm, _hostBasePath.Value);
            var options = new MemoryCacheEntryOptions()
                .SetSlidingExpiration(IdleTimeout)
                .RegisterPostEvictionCallback((k, v, reason, state) => {
                    if (v is HostEntry he) he.Dispose();
                });
            _cache.Set(key, entry, options);
            _activeKeys.Add(key);
            return entry;
        }
    }

    private string ResolveHostTfm(string targetTfm, string? csprojPath) {
        if (!string.Equals(targetTfm, "auto", StringComparison.OrdinalIgnoreCase)) {
            // Check if it's already a host TFM
            foreach (var h in HostTfms) {
                if (string.Equals(targetTfm, h, StringComparison.OrdinalIgnoreCase))
                    return h;
            }
            // Otherwise map it
            return MapToHostTfm(targetTfm);
        }

        // Auto-detect from csproj
        if (string.IsNullOrEmpty(csprojPath))
            throw new ArgumentException(
                "csprojPath is required when targetTfm is 'auto'. " +
                "No .csproj file could be found. You can bypass auto-detection by setting " +
                "the TFM environment variable (e.g. TFM=net48 or TFM=net8.0-windows).");

        var projectTfm = DetectTfmFromCsproj(csprojPath);
        return MapToHostTfm(projectTfm);
    }

    private static string DetectHostBasePath() {
        // Walk up from this assembly's location to find the RendererHost build output.
        // Layout: .../<config>/<tfm>/winformsmcp.dll
        // RendererHost: .../Rhombus.WinFormsMcp.RendererHost/bin/<config>/<tfm>/
        var serverDir = Path.GetDirectoryName(typeof(RendererProcessPool).Assembly.Location)!;

        // Try sibling project in source layout first
        // server: src/Rhombus.WinFormsMcp.Server/bin/Debug/net8.0-windows/
        // host:   src/Rhombus.WinFormsMcp.RendererHost/bin/Debug/
        var parts = serverDir.Replace('\\', '/').Split('/');
        for (int i = parts.Length - 1; i >= 0; i--) {
            if (string.Equals(parts[i], "bin", StringComparison.OrdinalIgnoreCase) && i >= 2) {
                // parts[i-1] = project name, parts[i] = "bin", parts[i+1] = config
                // Go up to the parent of the project dir (e.g. src/)
                var parentDir = string.Join("/", parts.Take(i - 1));
                var config = (i + 1 < parts.Length) ? parts[i + 1] : "Debug";
                var hostBin = Path.Combine(parentDir, "Rhombus.WinFormsMcp.RendererHost", "bin", config);
                if (Directory.Exists(hostBin))
                    return hostBin;
            }
        }

        // Try relative to assembly (published layout: tools/rendererhost/)
        var publishDir = Path.Combine(serverDir, "rendererhost");
        if (Directory.Exists(publishDir))
            return publishDir;

        throw new DirectoryNotFoundException(
            $"Cannot find RendererHost build output. Looked relative to: {serverDir}. " +
            "Build the RendererHost project or set hostBasePath explicitly.");
    }

    /// <summary>
    /// Manages a single host process for one TFM category.
    /// Thread-safe: uses a SemaphoreSlim to serialize requests to one process.
    /// </summary>
    private sealed class HostEntry : IDisposable {
        private readonly string _tfm;
        private readonly string _hostBasePath;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private Process? _process;
        private StreamWriter? _stdin;
        private StreamReader? _stdout;
        private bool _disposed;

        public HostEntry(string tfm, string hostBasePath) {
            _tfm = tfm;
            _hostBasePath = hostBasePath;
        }

        public async Task<byte[]> RenderAsync(string designerContent, string? companionContent, string[]? extraAssemblyPaths) {
            await _lock.WaitAsync();
            try {
                EnsureProcess();

                var request = JsonSerializer.Serialize(new {
                    designerContent,
                    companionContent,
                    extraAssemblyPaths
                }, JsonOptions);

                await _stdin!.WriteLineAsync(request);
                await _stdin.FlushAsync();

                var responseLine = await _stdout!.ReadLineAsync();
                if (responseLine == null)
                    throw new InvalidOperationException($"RendererHost ({_tfm}) closed unexpectedly.");

                using var doc = JsonDocument.Parse(responseLine);
                var root = doc.RootElement;

                if (root.TryGetProperty("success", out var successProp) && successProp.GetBoolean()) {
                    var base64 = root.GetProperty("pngBase64").GetString()
                        ?? throw new InvalidOperationException("Host returned success but no image data.");
                    return Convert.FromBase64String(base64);
                }

                var error = root.TryGetProperty("error", out var errProp) ? errProp.GetString() : "Unknown error";
                throw new InvalidOperationException($"RendererHost ({_tfm}) error: {error}");
            }
            catch {
                // If anything goes wrong, kill the process so next call starts fresh
                KillProcess();
                throw;
            }
            finally {
                _lock.Release();
            }
        }

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            KillProcess();
            _lock.Dispose();
        }

        private void EnsureProcess() {
            if (_process != null && !_process.HasExited)
                return;

            // Clean up dead process
            KillProcess();

            var exePath = FindHostExe();
            var psi = new ProcessStartInfo {
                FileName = exePath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exePath)!
            };

            _process = Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to start RendererHost for {_tfm}");

            _stdin = _process.StandardInput;
            _stdout = _process.StandardOutput;

            // Read the ready message
            var readyLine = _stdout.ReadLine();
            if (readyLine == null)
                throw new InvalidOperationException($"RendererHost ({_tfm}) exited before sending ready signal.");

            using var readyDoc = JsonDocument.Parse(readyLine);
            var type = readyDoc.RootElement.GetProperty("type").GetString();
            if (type != "ready")
                throw new InvalidOperationException($"RendererHost ({_tfm}) sent unexpected first message: {readyLine}");
        }

        private void KillProcess() {
            if (_process != null) {
                try {
                    _stdin?.Close();
                    if (!_process.HasExited)
                        _process.Kill();
                    _process.Dispose();
                }
                catch { /* best effort */ }
                _process = null;
                _stdin = null;
                _stdout = null;
            }
        }

        private string FindHostExe() {
            var exeName = "Rhombus.WinFormsMcp.RendererHost.exe";
            var exePath = Path.Combine(_hostBasePath, _tfm, exeName);
            if (File.Exists(exePath))
                return exePath;

            // Try without windows suffix (published layout may use short names)
            var shortTfm = _tfm.Replace("-windows", "");
            exePath = Path.Combine(_hostBasePath, shortTfm, exeName);
            if (File.Exists(exePath))
                return exePath;

            throw new FileNotFoundException(
                $"RendererHost executable not found for {_tfm}. " +
                $"Expected: {Path.Combine(_hostBasePath, _tfm, exeName)}");
        }

        private static readonly JsonSerializerOptions JsonOptions = new() {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }
}
