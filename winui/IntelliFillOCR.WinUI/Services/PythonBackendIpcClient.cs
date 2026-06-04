using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace IntelliFillOCR.WinUI.Services;

public sealed record BackendIpcResult(bool Success, string Title, string Message, string ResponseJson = "");

public sealed class PythonBackendIpcClient
{
    private readonly PythonBackendLauncher _backendLauncher;

    public PythonBackendIpcClient(PythonBackendLauncher backendLauncher)
    {
        _backendLauncher = backendLauncher;
    }

    public Task<BackendIpcResult> PingAsync(CancellationToken cancellationToken = default)
    {
        return InvokeOnceAsync("system.ping", new Dictionary<string, object?>(), cancellationToken);
    }

    public async Task<BackendIpcResult> InvokeOnceAsync(
        string command,
        IDictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        BackendProcessStartResult startResult = _backendLauncher.CreateIpcStartInfo();
        if (!startResult.Success || startResult.StartInfo is null)
        {
            return new BackendIpcResult(false, "IPC backend unavailable", startResult.Message);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            using Process process = Process.Start(startResult.StartInfo)
                ?? throw new InvalidOperationException("Could not start the Python IPC backend process.");

            string requestId = Guid.NewGuid().ToString("N");
            string requestJson = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["id"] = requestId,
                ["command"] = command,
                ["params"] = parameters ?? new Dictionary<string, object?>()
            });

            await process.StandardInput.WriteLineAsync(requestJson.AsMemory(), timeout.Token);
            process.StandardInput.Close();

            string? responseLine = await process.StandardOutput.ReadLineAsync(timeout.Token);
            string stderr = await process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);

            if (string.IsNullOrWhiteSpace(responseLine))
            {
                return new BackendIpcResult(false, "IPC backend did not respond", stderr.Trim());
            }

            return ParseResponse(responseLine);
        }
        catch (OperationCanceledException)
        {
            return new BackendIpcResult(false, "IPC backend timed out", "The Python backend did not respond within 30 seconds.");
        }
        catch (Exception ex)
        {
            return new BackendIpcResult(false, "IPC backend failed", ex.Message);
        }
    }

    public static BackendIpcResult ParseResponse(string responseLine)
    {
        using JsonDocument document = JsonDocument.Parse(responseLine);
        JsonElement root = document.RootElement;
        bool ok = root.TryGetProperty("ok", out JsonElement okElement) && okElement.GetBoolean();
        if (!ok)
        {
            string errorMessage = "Unknown IPC error.";
            if (root.TryGetProperty("error", out JsonElement error) &&
                error.TryGetProperty("message", out JsonElement message))
            {
                errorMessage = message.GetString() ?? errorMessage;
            }

            return new BackendIpcResult(false, "IPC command failed", errorMessage, responseLine);
        }

        string summary = "Python backend responded successfully.";
        if (root.TryGetProperty("result", out JsonElement result))
        {
            string version = result.TryGetProperty("version", out JsonElement versionElement)
                ? versionElement.GetString() ?? ""
                : "";
            string backend = result.TryGetProperty("backend", out JsonElement backendElement)
                ? backendElement.GetString() ?? ""
                : "";
            if (!string.IsNullOrWhiteSpace(version) || !string.IsNullOrWhiteSpace(backend))
            {
                summary = $"Connected to IntelliFill OCR {version} through {backend}.";
            }
        }

        return new BackendIpcResult(true, "IPC backend ready", summary, responseLine);
    }
}
