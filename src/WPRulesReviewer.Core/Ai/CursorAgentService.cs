using System.Diagnostics;
using System.Text;
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
        => RunPromptAsync(AiPromptBuilder.Build(report, _settings), ct);

    public Task<AiReportResult> GenerateAssignmentAnalysisAsync(SessionReport report, CancellationToken ct = default)
        => RunPromptAsync(AiPromptBuilder.BuildAssignmentAnalysis(report, _settings), ct);

    private async Task<AiReportResult> RunPromptAsync(string prompt, CancellationToken ct = default)
    {
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
        psi.ArgumentList.Add("text");
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
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
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

            var output = stdout.ToString().Trim();
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
