using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace IntelliFillOCR.WinUI.Services;

public sealed class PythonBackendIpcSession : IDisposable
{
    private readonly PythonBackendLauncher _backendLauncher;
    private readonly SemaphoreSlim _commandGate = new(1, 1);
    private Process? _process;
    private int _nextRequestId;
    private string _lastErrorOutput = "";

    public PythonBackendIpcSession(PythonBackendLauncher backendLauncher)
    {
        _backendLauncher = backendLauncher;
    }

    public bool IsRunning => _process is { HasExited: false };

    public Task<BackendIpcResult> PingAsync(CancellationToken cancellationToken = default)
    {
        return InvokeAsync("system.ping", new Dictionary<string, object?>(), cancellationToken);
    }

    public async Task<BackendIpcResult> InvokeAsync(
        string command,
        IDictionary<string, object?>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        await _commandGate.WaitAsync(cancellationToken);
        try
        {
            BackendIpcResult startResult = EnsureStarted();
            if (!startResult.Success)
            {
                return startResult;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(120));

            string requestJson = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["id"] = Interlocked.Increment(ref _nextRequestId).ToString(),
                ["command"] = command,
                ["params"] = parameters ?? new Dictionary<string, object?>()
            });

            await _process!.StandardInput.WriteLineAsync(requestJson.AsMemory(), timeout.Token);
            await _process.StandardInput.FlushAsync(timeout.Token);

            string? responseLine = await _process.StandardOutput.ReadLineAsync(timeout.Token);
            if (string.IsNullOrWhiteSpace(responseLine))
            {
                string message = string.IsNullOrWhiteSpace(_lastErrorOutput)
                    ? "The Python backend did not return a JSON response."
                    : _lastErrorOutput;
                return new BackendIpcResult(false, "IPC backend did not respond", message);
            }

            return PythonBackendIpcClient.ParseResponse(responseLine);
        }
        catch (OperationCanceledException)
        {
            return new BackendIpcResult(false, "IPC backend timed out", "The Python backend did not respond before the command timeout.");
        }
        catch (Exception ex)
        {
            Stop();
            return new BackendIpcResult(false, "IPC backend failed", ex.Message);
        }
        finally
        {
            _commandGate.Release();
        }
    }

    public void Dispose()
    {
        Stop();
        _commandGate.Dispose();
    }

    private BackendIpcResult EnsureStarted()
    {
        if (IsRunning)
        {
            return new BackendIpcResult(true, "IPC backend running", "The Python backend session is active.");
        }

        Stop();
        BackendProcessStartResult startResult = _backendLauncher.CreateIpcStartInfo();
        if (!startResult.Success || startResult.StartInfo is null)
        {
            return new BackendIpcResult(false, "IPC backend unavailable", startResult.Message);
        }

        try
        {
            _process = Process.Start(startResult.StartInfo);
            if (_process is null)
            {
                return new BackendIpcResult(false, "IPC backend failed", "Could not start the Python IPC backend process.");
            }

            _lastErrorOutput = "";
            _process.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                {
                    _lastErrorOutput = args.Data;
                }
            };
            _process.BeginErrorReadLine();
            return new BackendIpcResult(true, "IPC backend started", startResult.Message);
        }
        catch (Exception ex)
        {
            Stop();
            return new BackendIpcResult(false, "IPC backend failed", ex.Message);
        }
    }

    private void Stop()
    {
        if (_process is null)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.StandardInput.Close();
                if (!_process.WaitForExit(1500))
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
        }
        catch
        {
            // The backend may already be gone; shutdown should stay quiet.
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }
}
