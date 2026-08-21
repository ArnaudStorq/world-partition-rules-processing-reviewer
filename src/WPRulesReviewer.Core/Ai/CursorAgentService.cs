using System.Diagnostics;
using System.Text;
using System.Text.Json;
using WPRulesReviewer.Core.Logging;
using WPRulesReviewer.Core.Models;

namespace WPRulesReviewer.Core.Ai;

/// <summary>
/// Drives the local Cursor agent (cursor-agent CLI) in headless print mode to turn the
/// triaged anomalies into a human-readable report. Fully optional: if the CLI is missing
/// or AI is disabled, the call reports gracefully instead of throwing.
/// </summary>
public sealed class CursorAgentService : IAiAgentService
{
    private readonly AppSettings _settings;
    private readonly IActivityLog _log;

    public CursorAgentService(AppSettings settings, IActivityLog log)
    {
        _settings = settings;
        _log = log;
    }

    public bool IsAvailable => _settings.EnableAi && ResolveExecutable() is not null;

    public Task<AiReportResult> GenerateReportAsync(SessionReport report, CancellationToken ct = default)
        => RunPromptAsync(AiPromptBuilder.Build(report, _settings), null, ct);

    public Task<AiReportResult> GenerateAssignmentAnalysisAsync(SessionReport report, CancellationToken ct = default)
        => RunPromptAsync(AiPromptBuilder.BuildAssignmentAnalysis(report, _settings), null, ct);

    public Task<AiReportResult> CompleteAsync(string prompt, CancellationToken ct = default)
        => RunPromptAsync(prompt, null, ct);

    public Task<AiReportResult> CompleteStreamingAsync(string prompt, IProgress<string>? onOutput, CancellationToken ct = default)
        => RunPromptAsync(prompt, onOutput, ct);

    private async Task<AiReportResult> RunPromptAsync(string prompt, IProgress<string>? onOutput, CancellationToken ct = default)
    {
        // When a live consumer is attached, stream the agent's text deltas as NDJSON so the reasoning
        // can be shown as it is generated; otherwise fall back to the simple aggregated text format.
        bool streaming = onOutput is not null;
        if (!_settings.EnableAi)
            return new AiReportResult { Success = false, Error = "AI is disabled in Settings." };

        var exe = ResolveExecutable();
        if (exe is null)
            return new AiReportResult
            {
                Success = false,
                Error = $"Cursor agent executable not found ('{_settings.CursorAgentPath}'). Set the correct path in Settings > AI."
            };

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add("--output-format");
        if (streaming)
        {
            psi.ArgumentList.Add("stream-json");
            psi.ArgumentList.Add("--stream-partial-output");
        }
        else
        {
            psi.ArgumentList.Add("text");
        }
        // Run headless without the interactive "Workspace Trust Required" prompt.
        psi.ArgumentList.Add("-f");
        if (!string.IsNullOrWhiteSpace(_settings.AiModel) && _settings.AiModel != "auto")
        {
            psi.ArgumentList.Add("--model");
            psi.ArgumentList.Add(_settings.AiModel);
        }

        _log.Info($"Invoking Cursor agent ({exe})...", "AI");

        try
        {
            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var stdout = new StringBuilder();   // raw stdout (NDJSON when streaming)
            var stderr = new StringBuilder();
            var assistantText = new StringBuilder(); // reconstructed human answer when streaming
            string? resultText = null;

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                stdout.AppendLine(e.Data);

                if (!streaming)
                {
                    onOutput?.Report(e.Data);
                    return;
                }

                // Parse one NDJSON event and forward assistant text deltas to the live consumer.
                var (delta, result) = ParseStreamEvent(e.Data);
                if (!string.IsNullOrEmpty(delta))
                {
                    assistantText.Append(delta);
                    onOutput?.Report(delta!);
                }
                if (result is not null) resultText = result;
            };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

            if (!process.Start())
                return new AiReportResult { Success = false, Error = "Failed to start the Cursor agent process." };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.StandardInput.WriteAsync(prompt).ConfigureAwait(false);
            process.StandardInput.Close();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(15, _settings.AiTimeoutSeconds)));

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(true); } catch { /* ignore */ }
                return new AiReportResult { Success = false, Error = "Cursor agent timed out." };
            }

            // In streaming mode the meaningful content is the reconstructed assistant answer (or the
            // final "result" event), not the raw NDJSON.
            var output = streaming
                ? (resultText ?? assistantText.ToString()).Trim()
                : stdout.ToString().Trim();

            if (process.ExitCode != 0 && output.Length == 0)
                return new AiReportResult { Success = false, Error = $"Cursor agent exited with code {process.ExitCode}. {stderr}".Trim() };

            _log.Success("Cursor agent report ready.", "AI");
            return new AiReportResult { Success = true, Content = output };
        }
        catch (Exception ex)
        {
            return new AiReportResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Parses one NDJSON line emitted by <c>--output-format stream-json --stream-partial-output</c>.
    /// Returns the incremental assistant text delta (if this is a streaming chunk) and/or the final
    /// aggregated result text (on the "result" event). Unknown/other events yield nothing.
    /// </summary>
    private static (string? delta, string? result) ParseStreamEvent(string line)
    {
        line = line.Trim();
        if (line.Length == 0 || line[0] != '{') return (null, null);

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeEl)) return (null, null);
            var type = typeEl.GetString();

            if (type == "result")
            {
                var result = root.TryGetProperty("result", out var r) ? r.GetString() : null;
                return (null, result);
            }

            if (type == "assistant")
            {
                // A streaming delta has a timestamp and no model_call_id. Skip buffered duplicates
                // (before a tool call) and the final flush at end of turn to avoid repeated text.
                bool hasTimestamp = root.TryGetProperty("timestamp_ms", out _);
                bool hasModelCall = root.TryGetProperty("model_call_id", out _);
                if (!hasTimestamp || hasModelCall) return (null, null);

                if (root.TryGetProperty("message", out var msg) &&
                    msg.TryGetProperty("content", out var content) &&
                    content.ValueKind == JsonValueKind.Array)
                {
                    var sb = new StringBuilder();
                    foreach (var part in content.EnumerateArray())
                    {
                        if (part.TryGetProperty("type", out var pt) && pt.GetString() == "text" &&
                            part.TryGetProperty("text", out var txt))
                        {
                            sb.Append(txt.GetString());
                        }
                    }
                    return (sb.Length > 0 ? sb.ToString() : null, null);
                }
            }
        }
        catch (JsonException)
        {
            // Ignore malformed / partial lines.
        }
        return (null, null);
    }

    private string? ResolveExecutable()
    {
        var configured = _settings.CursorAgentPath;
        if (string.IsNullOrWhiteSpace(configured)) return null;
        if (File.Exists(configured)) return configured;

        // Search PATH for the command (with common Windows extensions).
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var exts = new[] { "", ".cmd", ".exe", ".bat", ".ps1" };
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var ext in exts)
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim(), configured + ext);
                    if (File.Exists(candidate)) return candidate;
                }
                catch { /* ignore malformed PATH entries */ }
            }
        }
        return null;
    }
}
