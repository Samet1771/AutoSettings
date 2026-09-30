using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutoSettings.Core;
using AutoSettings.Core.Engine;
using AutoSettings.Core.Updates;
using AutoSettings.Platform.Security;
using AutoSettings.Platform.Updates;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoSettings.Service;

/// <summary>Settings from appsettings.json, section "Updates".</summary>
public sealed class UpdateOptions
{
    /// <summary>Set to false to turn updates off completely (users cannot turn them on).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The GitHub repository (<c>owner/name</c>) whose releases are installed.</summary>
    public string Repository { get; set; } = "Samet1771/AutoSettings";

    /// <summary>Hours between checks.</summary>
    public double CheckIntervalHours { get; set; } = 12;

    /// <summary>Mode used until someone changes it in the app.</summary>
    public UpdateMode DefaultMode { get; set; } = UpdateMode.AskFirst;

    /// <summary>Offer beta versions until someone changes it in the app.</summary>
    public bool DefaultIncludePrereleases { get; set; }

    /// <summary>Whether users may change the update settings in the app. Set to false to lock them (company PCs).</summary>
    public bool AllowUserChanges { get; set; } = true;
}

/// <summary>
/// Checks GitHub Releases for new versions, downloads and verifies the MSI, and installs it with msiexec
/// (as SYSTEM, so no elevation prompt). See docs/guide/updates.md and docs/dev/security.md.
/// </summary>
public sealed class UpdateService : BackgroundService
{
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan BusyRetry = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MaxAutomaticDelay = TimeSpan.FromHours(24);
    private static readonly TimeSpan AgentExitGrace = TimeSpan.FromSeconds(8);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly UpdateOptions _options;
    private readonly AgentHub _hub;
    private readonly AgentSupervisor _supervisor;
    private readonly ActivityLog _activity;
    private readonly ILogger<UpdateService> _logger;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly SemVersion _current;
    private readonly InstallMethod _method;
    private StoredState _stored;

    private UpdateState _state = UpdateState.Unknown;
    private ReleaseInfo? _latest;
    private string? _message;
    private string? _downloadedPath;
    private string? _downloadedVersion;
    private DateTimeOffset _downloadedAt;

    public UpdateService(IOptions<UpdateOptions> options, AgentHub hub, AgentSupervisor supervisor, ActivityLog activity, ILogger<UpdateService> logger)
    {
        _options = options.Value;
        _hub = hub;
        _supervisor = supervisor;
        _activity = activity;
        _logger = logger;
        _current = CurrentVersion();
        _method = InstallInfo.Detect();
        _stored = LoadState();

        _http = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        })
        {
            Timeout = TimeSpan.FromMinutes(15),
        };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(Product.Name, _current.ToString()));
    }

    private static string UpdatesDirectory => Path.Combine(Product.MachineDataDirectory, "updates");

    private static string StatePath => Path.Combine(Product.MachineDataDirectory, "updates.json");

    private UpdateSettings Settings => new(
        _options.Enabled ? _stored.Mode ?? _options.DefaultMode : UpdateMode.Off,
        _stored.IncludePrereleases ?? _options.DefaultIncludePrereleases);

    /// <summary>The status shown in the app.</summary>
    public UpdateStatus Status()
    {
        var settings = Settings;
        var state = settings.Mode == UpdateMode.Off && _state is UpdateState.Unknown or UpdateState.UpToDate ? UpdateState.Disabled : _state;
        var message = _message;
        if (message is null && _method == InstallMethod.Portable)
            message = "AutoSettings was installed without the MSI installer, so it can only tell you about new versions. Install the MSI to get automatic updates.";
        if (message is null && !_options.Enabled)
            message = "Updates were turned off by the administrator.";
        return new UpdateStatus(
            _current.ToString(),
            state,
            _latest,
            _stored.LastChecked,
            message,
            settings,
            CanChangeSettings: _options.Enabled && _options.AllowUserChanges,
            CanInstall: _options.Enabled && UpdatePlanner.CanInstall(_method, _latest, state));
    }

    /// <summary>Checks GitHub now (from the app).</summary>
    public async Task<UpdateStatus> CheckNowAsync(CancellationToken cancellationToken)
    {
        if (_options.Enabled)
            await CheckAsync(userRequested: true, cancellationToken).ConfigureAwait(false);
        return Status();
    }

    /// <summary>Installs the available update (from the app). Returns an error message or null.</summary>
    public async Task<string?> InstallNowAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            return "Updates were turned off by the administrator.";
        if (_method != InstallMethod.Msi)
            return "This installation cannot update itself. Download the installer from the release page.";

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_latest is not { } release || !UpdatePlanner.CanInstall(_method, release, _state))
                return "There is no update to install.";
            if (!IsDownloaded(release))
            {
                var error = await DownloadAsync(release, cancellationToken).ConfigureAwait(false);
                if (error is not null)
                    return error;
            }
            return await StartInstallerAsync(release, automatic: false).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Changes the settings (from the app). Returns an error message or null.</summary>
    public async Task<string?> ChangeSettingsAsync(UpdateSettings settings, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            return "Updates were turned off by the administrator.";
        if (!_options.AllowUserChanges)
            return "The update settings were locked by the administrator.";

        await UpdateStoredAsync(s => s with { Mode = settings.Mode, IncludePrereleases = settings.IncludePrereleases }, cancellationToken).ConfigureAwait(false);
        _activity.Info(ActivitySources.Update, $"Update settings changed: {Describe(settings)}.");
        if (settings.Mode != UpdateMode.Off)
            Wake();
        Broadcast();
        return null;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ReportPreviousInstallAsync(stoppingToken).ConfigureAwait(false);
        CleanUpOldDownloads();

        await WaitAsync(FirstCheckDelay, stoppingToken).ConfigureAwait(false);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_options.Enabled && Settings.Mode != UpdateMode.Off)
                    await CheckAsync(userRequested: false, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Update check failed");
            }

            var waitingToInstall = Settings.Mode == UpdateMode.Automatic && _state == UpdateState.Ready;
            var jitter = TimeSpan.FromMinutes(Random.Shared.Next(-30, 31));
            var interval = TimeSpan.FromHours(Math.Max(1, _options.CheckIntervalHours)) + jitter;
            await WaitAsync(waitingToInstall ? BusyRetry : interval, stoppingToken).ConfigureAwait(false);
        }
    }

    // ------------------------------------------------------------------ checking

    private async Task CheckAsync(bool userRequested, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == UpdateState.Installing)
                return;

            var settings = Settings;
            SetState(UpdateState.Checking, null);
            ReleaseInfo? release;
            try
            {
                release = await FetchLatestAsync(settings.IncludePrereleases, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Could not check for updates: {Message}", ex.Message);
                SetState(UpdateState.Failed, $"Could not check for updates: {Explain(ex)}");
                if (userRequested)
                    _activity.Warning(ActivitySources.Update, _message!);
                return;
            }

            await UpdateStoredAsync(s => s with { LastChecked = DateTimeOffset.Now }, cancellationToken).ConfigureAwait(false);
            _latest = release;
            if (release is null)
            {
                SetState(UpdateState.UpToDate, null);
                return;
            }

            var downloaded = IsDownloaded(release);
            SetState(downloaded ? UpdateState.Ready : UpdateState.Available, null);
            if (_stored.AnnouncedVersion != release.Version)
            {
                _activity.Info(ActivitySources.Update, $"{Product.Name} {release.Version} is available (installed: {_current}).");
                await UpdateStoredAsync(s => s with { AnnouncedVersion = release.Version }, cancellationToken).ConfigureAwait(false);
            }

            var step = UpdatePlanner.NextStep(settings.Mode, _method, release, downloaded);
            if (step == UpdateStep.Download)
            {
                var error = await DownloadAsync(release, cancellationToken).ConfigureAwait(false);
                if (error is not null)
                    return;
                step = UpdatePlanner.NextStep(settings.Mode, _method, release, downloaded: true);
            }
            if (step == UpdateStep.Install)
                await TryAutomaticInstallAsync(release, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ReleaseInfo?> FetchLatestAsync(bool includePrereleases, CancellationToken cancellationToken)
    {
        var repository = _options.Repository.Trim().Trim('/');
        if (repository.Split('/').Length != 2)
            throw new InvalidOperationException($"'{repository}' is not a GitHub repository like owner/name.");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases?per_page=20");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase}.");
        var json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        return ReleaseFeed.FindUpdate(json, _current, includePrereleases);
    }

    // ------------------------------------------------------------------ downloading and verifying

    private bool IsDownloaded(ReleaseInfo release) =>
        _downloadedVersion == release.Version && _downloadedPath is not null && File.Exists(_downloadedPath);

    /// <summary>Downloads and verifies the MSI. Returns an error message, or null when it is ready to install.</summary>
    private async Task<string?> DownloadAsync(ReleaseInfo release, CancellationToken cancellationToken)
    {
        SetState(UpdateState.Downloading, null);
        var directory = Path.Combine(UpdatesDirectory, release.Version);
        var fileName = Path.GetFileName(release.MsiName);
        var target = Path.Combine(directory, fileName);
        var partial = target + ".partial";
        try
        {
            if (!ReleaseFeed.IsInstaller(fileName))
                throw new UpdateException($"'{fileName}' is not an {Product.Name} installer.");
            if (!IsGitHubDownload(release.MsiUrl) || release.ChecksumsUrl is { } sums && !IsGitHubDownload(sums))
                throw new UpdateException("The download address is not on github.com.");

            // Only SYSTEM and administrators may write here, so nobody can swap a verified installer before it runs.
            SecureDirectory.Create(UpdatesDirectory);
            SecureDirectory.Create(directory);

            string actual;
            await using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                using var response = await _http.GetAsync(release.MsiUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    sha.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
                actual = Convert.ToHexStringLower(sha.GetHashAndReset());
            }

            await VerifyChecksumAsync(release, fileName, actual, cancellationToken).ConfigureAwait(false);
            // Windows picks the signature format by file type, so check the signature under the .msi name.
            File.Move(partial, target, overwrite: true);
            VerifySignature(target);

            _downloadedPath = target;
            _downloadedVersion = release.Version;
            _downloadedAt = DateTimeOffset.Now;
            SetState(UpdateState.Ready, null);
            _activity.Info(ActivitySources.Update, $"Downloaded and verified {Product.Name} {release.Version}.");
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            TryDelete(partial);
            TryDelete(target);
            _downloadedPath = null;
            _downloadedVersion = null;
            var message = $"The update to {release.Version} could not be downloaded: {Explain(ex)}";
            _logger.LogWarning(ex, "Update download failed");
            _activity.Error(ActivitySources.Update, message);
            SetState(UpdateState.Failed, message);
            return message;
        }
    }

    private async Task VerifyChecksumAsync(ReleaseInfo release, string fileName, string actual, CancellationToken cancellationToken)
    {
        var verified = false;
        if (release.ApiSha256 is { } apiSha)
        {
            if (!string.Equals(apiSha, actual, StringComparison.OrdinalIgnoreCase))
                throw new UpdateException("the file does not match the checksum GitHub reports for it.");
            verified = true;
        }
        if (release.ChecksumsUrl is { } url)
        {
            var text = await _http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            if (!Checksums.Parse(text).TryGetValue(fileName, out var expected))
                throw new UpdateException($"{ReleaseFeed.ChecksumsFileName} has no entry for {fileName}.");
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                throw new UpdateException($"the file does not match {ReleaseFeed.ChecksumsFileName}.");
            verified = true;
        }
        if (!verified)
            throw new UpdateException("the release has no checksum, so the file cannot be verified.");
    }

    /// <summary>A signed installation only accepts updates signed with the same certificate.</summary>
    private static void VerifySignature(string msiPath)
    {
        var mySigner = Environment.ProcessPath is { } exe ? Authenticode.Signer(exe) : null;
        if (mySigner is null)
            return;
        var theirSigner = Authenticode.TrustedSigner(msiPath);
        if (theirSigner is null)
            throw new UpdateException("the installer is not signed (or its signature is not trusted), but this installation is.");
        if (!string.Equals(mySigner, theirSigner, StringComparison.Ordinal))
            throw new UpdateException($"the installer is signed by '{theirSigner}', not by '{mySigner}'.");
    }

    private static bool IsGitHubDownload(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));

    // ------------------------------------------------------------------ installing

    private async Task TryAutomaticInstallAsync(ReleaseInfo release, CancellationToken cancellationToken)
    {
        var busy = await AnyoneBusyAsync().ConfigureAwait(false);
        if (!UpdatePlanner.MayInstallAutomatically(busy, _downloadedAt, DateTimeOffset.Now, MaxAutomaticDelay))
        {
            _message = "The update will be installed when no full-screen app (game, presentation) is running.";
            Broadcast();
            return;
        }
        await StartInstallerAsync(release, automatic: true).ConfigureAwait(false);
    }

    private async Task<bool> AnyoneBusyAsync()
    {
        var checks = _hub.All().Select(async api =>
        {
            try
            {
                return await api.IsBusyAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
            catch
            {
                return false;
            }
        });
        var results = await Task.WhenAll(checks).ConfigureAwait(false);
        return results.Any(b => b);
    }

    /// <summary>Closes the agents, then starts msiexec. Returns an error message or null.</summary>
    private async Task<string?> StartInstallerAsync(ReleaseInfo release, bool automatic)
    {
        if (_downloadedPath is not { } msi || !File.Exists(msi))
            return "The update is not downloaded.";

        var log = Path.Combine(UpdatesDirectory, $"install-{release.Version}.log");
        try
        {
            SaveStored(_stored with { Pending = new PendingInstall(_current.ToString(), release.Version, log, DateTimeOffset.Now) });
            _activity.Info(ActivitySources.Update, automatic
                ? $"Installing {Product.Name} {release.Version} automatically. {Product.Name} restarts in a moment."
                : $"Installing {Product.Name} {release.Version}. {Product.Name} restarts in a moment.");

            // Agents exit when they see this state, so the installer can replace their files.
            SetState(UpdateState.Installing, null);
            await Task.Delay(AgentExitGrace).ConfigureAwait(false);

            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "msiexec.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in new[] { "/i", msi, "/qn", "/norestart", "/l*v", log })
                start.ArgumentList.Add(argument);
            // msiexec outlives this service: the installer stops and restarts it.
            using var process = Process.Start(start) ?? throw new UpdateException("msiexec did not start.");
            _logger.LogInformation("Started msiexec (pid {Pid}) for {Version}", process.Id, release.Version);
            return null;
        }
        catch (Exception ex)
        {
            SaveStored(_stored with { Pending = null });
            var message = $"The update to {release.Version} could not be started: {Explain(ex)}";
            _activity.Error(ActivitySources.Update, message);
            SetState(UpdateState.Failed, message);
            _supervisor.StartForExistingSessions();
            return message;
        }
    }

    /// <summary>After a restart: reports whether the install that was started before succeeded.</summary>
    private async Task ReportPreviousInstallAsync(CancellationToken cancellationToken)
    {
        if (_stored.Pending is not { } pending)
            return;
        var target = SemVersion.TryParse(pending.To);
        if (target is not null && _current >= target)
        {
            _activity.Success(ActivitySources.Update, $"{Product.Name} was updated from {pending.From} to {_current}.");
            _logger.LogInformation("Updated from {From} to {To}", pending.From, _current);
        }
        else
        {
            var message = $"The update to {pending.To} did not complete; {Product.Name} {_current} is still installed. Details: {pending.Log}";
            _activity.Error(ActivitySources.Update, message);
            _message = message;
        }
        await UpdateStoredAsync(s => s with { Pending = null }, cancellationToken).ConfigureAwait(false);
    }

    private void CleanUpOldDownloads()
    {
        try
        {
            if (!Directory.Exists(UpdatesDirectory))
                return;
            foreach (var directory in Directory.GetDirectories(UpdatesDirectory))
            {
                var version = SemVersion.TryParse(Path.GetFileName(directory));
                if (version is not null && version <= _current)
                    Directory.Delete(directory, recursive: true);
            }
            foreach (var log in Directory.GetFiles(UpdatesDirectory, "install-*.log").Where(f => File.GetLastWriteTime(f) < DateTime.Now.AddDays(-30)))
                File.Delete(log);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not clean up old updates");
        }
    }

    // ------------------------------------------------------------------ helpers

    private void SetState(UpdateState state, string? message)
    {
        _state = state;
        _message = message;
        Broadcast();
    }

    private void Broadcast()
    {
        var status = Status();
        _hub.Broadcast(api => api.OnUpdateStatusChangedAsync(status, CancellationToken.None), "update status");
    }

    private void Wake()
    {
        try
        {
            if (_wake.CurrentCount == 0)
                _wake.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    private async Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await _wake.WaitAsync(delay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static SemVersion CurrentVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(UpdateService).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return SemVersion.TryParse(informational)
            ?? SemVersion.TryParse(assembly.GetName().Version?.ToString(3))
            ?? SemVersion.Parse("0.0.0");
    }

    private static string Describe(UpdateSettings settings) =>
        settings.Mode switch
        {
            UpdateMode.Off => "off",
            UpdateMode.Notify => "only notify",
            UpdateMode.AskFirst => "ask before installing",
            _ => "install automatically",
        } + (settings.IncludePrereleases ? ", including beta versions" : "");

    private static string Explain(Exception ex) => ex switch
    {
        UpdateException => ex.Message,
        HttpRequestException http => http.Message,
        TaskCanceledException => "the connection timed out.",
        _ => ex.Message,
    };

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    // ------------------------------------------------------------------ persisted state

    private async Task UpdateStoredAsync(Func<StoredState, StoredState> change, CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SaveStored(change(_stored));
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private void SaveStored(StoredState state)
    {
        _stored = state;
        try
        {
            Directory.CreateDirectory(Product.MachineDataDirectory);
            var temp = StatePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(state, JsonOptions));
            File.Move(temp, StatePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not save {Path}", StatePath);
        }
    }

    private StoredState LoadState()
    {
        try
        {
            if (File.Exists(StatePath))
                return JsonSerializer.Deserialize<StoredState>(File.ReadAllText(StatePath), JsonOptions) ?? new StoredState();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read {Path}; using defaults", StatePath);
        }
        return new StoredState();
    }

    public override void Dispose()
    {
        _http.Dispose();
        base.Dispose();
    }

    /// <summary>What <c>%ProgramData%\AutoSettings\updates.json</c> contains.</summary>
    internal sealed record StoredState
    {
        public UpdateMode? Mode { get; init; }
        public bool? IncludePrereleases { get; init; }
        public DateTimeOffset? LastChecked { get; init; }
        public string? AnnouncedVersion { get; init; }
        public PendingInstall? Pending { get; init; }
    }

    /// <summary>An install started before the service stopped for it.</summary>
    internal sealed record PendingInstall(string From, string To, string Log, DateTimeOffset StartedAt);

    private sealed class UpdateException(string message) : Exception(message);
}
