using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using WPRulesReviewer.Core.Models;

namespace WPRulesReviewer.Core.TeamCity;

public sealed class TeamCityArtifact
{
    public string Name { get; set; } = string.Empty;
    public long Size { get; set; }
    public string ContentHref { get; set; } = string.Empty;
}

/// <summary>Minimal TeamCity REST client for listing builds and downloading log artifacts.</summary>
public sealed class TeamCityClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly AppSettings _settings;
    private readonly Regex _sessionRegex;

    public TeamCityClient(AppSettings settings)
    {
        _settings = settings;

        var handler = new HttpClientHandler();
        if (!settings.TeamCityVerifySsl)
            handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;

        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(settings.TeamCityBaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(120)
        };
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(settings.TeamCityToken))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.TeamCityToken);
        else if (!string.IsNullOrWhiteSpace(settings.TeamCityUsername))
        {
            var raw = Encoding.UTF8.GetBytes($"{settings.TeamCityUsername}:{settings.TeamCityPassword}");
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
        }

        try { _sessionRegex = new Regex(settings.TeamCitySessionTagRegex, RegexOptions.Compiled); }
        catch { _sessionRegex = new Regex(@"\(([^)]+)\)", RegexOptions.Compiled); }
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_settings.TeamCityBaseUrl) &&
        !string.IsNullOrWhiteSpace(_settings.TeamCityBuildTypeId) &&
        (!string.IsNullOrWhiteSpace(_settings.TeamCityToken) || !string.IsNullOrWhiteSpace(_settings.TeamCityUsername));

    public async Task<List<TeamCityBuild>> GetRecentBuildsAsync(CancellationToken ct = default)
    {
        var count = Math.Clamp(_settings.TeamCityMaxBuilds, 1, 500);
        var locator = $"buildType:{_settings.TeamCityBuildTypeId},count:{count}";
        if (_settings.TeamCityOnlySuccessful) locator += ",status:SUCCESS";
        var url = $"app/rest/builds?locator={Uri.EscapeDataString(locator)}" +
                  "&fields=build(id,number,status,state,startDate,finishDate,webUrl,tags(tag(name)),comment(text))";

        using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var dto = await JsonSerializer.DeserializeAsync<BuildsDto>(stream, JsonOpts, ct).ConfigureAwait(false);

        var cutoff = _settings.TeamCityLookbackHours > 0
            ? DateTimeOffset.Now.AddHours(-_settings.TeamCityLookbackHours)
            : (DateTimeOffset?)null;

        var result = new List<TeamCityBuild>();
        foreach (var b in dto?.Build ?? new())
        {
            var build = new TeamCityBuild
            {
                Id = b.Id,
                Number = b.Number ?? string.Empty,
                BuildTypeId = _settings.TeamCityBuildTypeId,
                Status = b.Status ?? string.Empty,
                State = b.State ?? string.Empty,
                StartDate = ParseTcDate(b.StartDate),
                FinishDate = ParseTcDate(b.FinishDate),
                WebUrl = b.WebUrl ?? string.Empty
            };
            if (b.Tags?.Tag is { } tags)
                foreach (var t in tags) if (!string.IsNullOrWhiteSpace(t.Name)) build.Tags.Add(t.Name!);

            build.SessionName = DeriveSessionName(build, b.Comment?.Text);

            if (cutoff is { } c && build.StartDate is { } sd && sd < c) continue;
            result.Add(build);
        }
        return result;
    }

    public async Task<List<TeamCityArtifact>> ListLogArtifactsAsync(long buildId, CancellationToken ct = default)
    {
        var folder = _settings.TeamCityArtifactLogPath.Trim('/');
        var url = $"app/rest/builds/id:{buildId}/artifacts/children/{folder}?fields=file(name,size,content(href))";
        using var resp = await _http.GetAsync(url, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var dto = await JsonSerializer.DeserializeAsync<FilesDto>(stream, JsonOpts, ct).ConfigureAwait(false);

        var list = new List<TeamCityArtifact>();
        foreach (var f in dto?.File ?? new())
        {
            if (f.Name is null || !f.Name.EndsWith(".log", StringComparison.OrdinalIgnoreCase)) continue;
            list.Add(new TeamCityArtifact { Name = f.Name, Size = f.Size, ContentHref = f.Content?.Href ?? string.Empty });
        }
        return list;
    }

    public async Task<string> DownloadArtifactAsync(long buildId, string artifactName, string destinationFolder,
        IProgress<long>? progress = null, CancellationToken ct = default, string? destinationFileName = null)
    {
        Directory.CreateDirectory(destinationFolder);
        var folder = _settings.TeamCityArtifactLogPath.Trim('/');
        var url = $"app/rest/builds/id:{buildId}/artifacts/content/{folder}/{Uri.EscapeDataString(artifactName)}";

        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();

        var destPath = Path.Combine(destinationFolder, string.IsNullOrWhiteSpace(destinationFileName) ? artifactName : destinationFileName);
        await using var input = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var output = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            total += read;
            progress?.Report(total);
        }
        return destPath;
    }

    private string DeriveSessionName(TeamCityBuild build, string? comment)
    {
        if (build.Tags.Count > 0) return build.Tags[0];
        foreach (var candidate in new[] { comment, build.Number })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            var m = _sessionRegex.Match(candidate);
            if (m.Success && m.Groups.Count > 1) return m.Groups[1].Value.Trim();
        }
        return string.IsNullOrWhiteSpace(build.Number) ? $"Build {build.Id}" : $"#{build.Number}";
    }

    private static DateTimeOffset? ParseTcDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        // TeamCity format: 20260808T003445-0400
        return DateTimeOffset.TryParseExact(s, "yyyyMMdd'T'HHmmsszzz", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var dto) ? dto : null;
    }

    public void Dispose() => _http.Dispose();

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    // ---- DTOs --------------------------------------------------------------
    private sealed class BuildsDto { [JsonPropertyName("build")] public List<BuildDto> Build { get; set; } = new(); }
    private sealed class BuildDto
    {
        public long Id { get; set; }
        public string? Number { get; set; }
        public string? Status { get; set; }
        public string? State { get; set; }
        public string? StartDate { get; set; }
        public string? FinishDate { get; set; }
        public string? WebUrl { get; set; }
        public TagsDto? Tags { get; set; }
        public CommentDto? Comment { get; set; }
    }
    private sealed class TagsDto { [JsonPropertyName("tag")] public List<TagDto>? Tag { get; set; } }
    private sealed class TagDto { public string? Name { get; set; } }
    private sealed class CommentDto { public string? Text { get; set; } }
    private sealed class FilesDto { [JsonPropertyName("file")] public List<FileDto> File { get; set; } = new(); }
    private sealed class FileDto { public string? Name { get; set; } public long Size { get; set; } public ContentDto? Content { get; set; } }
    private sealed class ContentDto { public string? Href { get; set; } }
}
