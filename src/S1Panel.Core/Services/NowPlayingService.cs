using SkiaSharp;

namespace S1Panel.Core.Services;

/// <summary>What a media app is playing right now. <see cref="Art"/> is a small (≤160 px) decoded thumbnail, if the app supplies one.</summary>
public sealed record MediaInfo(
    string Title, string Artist, string Album, string App, bool Playing,
    TimeSpan Position, TimeSpan Duration, DateTime PositionAtUtc, SKImage? Art, string AppId = "")
{
    /// <summary>Playback position now, advancing from the last report while playing.</summary>
    public TimeSpan PositionNow(DateTime utcNow)
    {
        var p = Playing ? Position + (utcNow - PositionAtUtc) : Position;
        if (p < TimeSpan.Zero) p = TimeSpan.Zero;
        return Duration > TimeSpan.Zero && p > Duration ? Duration : p;
    }
}

/// <param name="Ready">False until the first check has finished.</param>
/// <param name="Problem">Set when Windows' media sessions can't be read at all (e.g. no desktop session over ssh).</param>
public sealed record NowPlayingState(MediaInfo? Media, bool Ready, string? Problem);

/// <summary>
/// The media Windows shows in its volume/media flyout (Spotify, browsers, Media Player...), via the
/// GlobalSystemMediaTransportControls API: no admin, no network. Polled every 2 s in the background while
/// something asks for it, and stops polling 30 s after the last request.
/// </summary>
public static class NowPlayingService
{
    private static readonly object Gate = new();
    private static NowPlayingState _state = new(null, false, null);
    private static DateTime _lastAskedUtc;
    private static bool _running;

    private static DateTime _lastMediaUtc;

    private const string Idle = "idle";
    private static volatile string _stage = Idle;
    private static DateTime _pollStartedUtc;
    private static bool _reportedStuck;
    private static string? _lastProblem;

    /// <summary>Problems worth logging: a check that failed (once per distinct error) or got stuck (with the step).</summary>
    public static event Action<string>? Diagnostic;

    private static void Stage(string what) => _stage = what;

    public static TimeSpan PollEvery { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>How long the last track stays on screen after its session disappears.</summary>
    public static TimeSpan HoldLastFor { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Replaceable for tests: returns the current media, or null when nothing is playing.</summary>
    public static Func<CancellationToken, Task<MediaInfo?>> Source { get; set; } = WindowsMedia.QueryAsync;

    /// <summary>Latest known media state; starts background polling if needed. Never blocks.</summary>
    public static NowPlayingState Get()
    {
        lock (Gate)
        {
            _lastAskedUtc = DateTime.UtcNow;
            string stage = _stage;
            if (stage != Idle && !_reportedStuck && DateTime.UtcNow - _pollStartedUtc > TimeSpan.FromSeconds(15))
            {
                _reportedStuck = true;
                Diagnostic?.Invoke($"Media check stuck for {(DateTime.UtcNow - _pollStartedUtc).TotalSeconds:0} s while {stage}.");
            }
            if (!_running)
            {
                _running = true;
                _ = Task.Run(PollLoop);
            }
            return _state;
        }
    }

    /// <summary>Forgets cached state (tests).</summary>
    internal static void Reset()
    {
        lock (Gate) _state = new(null, false, null);
    }

    private static async Task PollLoop()
    {
        while (true)
        {
            NowPlayingState next;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                _pollStartedUtc = DateTime.UtcNow;
                Stage("starting");
                next = new NowPlayingState(await Source(cts.Token), true, null);
                _lastProblem = null;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // COMException/UnauthorizedAccess when there's no interactive desktop, timeouts, odd app data...
                next = new NowPlayingState(null, true, "Media info unavailable");
                string problem = $"Media check failed while {_stage}: {ex.GetType().Name}: {ex.Message.Split('\n')[0].Trim()}";
                if (problem != _lastProblem)
                    Diagnostic?.Invoke(_lastProblem = problem);
            }
            finally
            {
                Stage(Idle);
                _reportedStuck = false;
            }

            lock (Gate)
            {
                if (next.Media is not null)
                    _lastMediaUtc = DateTime.UtcNow;
                else if (_state.Media is { } last && next.Problem is null)
                    next = Hold(last, DateTime.UtcNow) is { } held ? new NowPlayingState(held, true, null) : next;
                _state = next;
                if (DateTime.UtcNow - _lastAskedUtc > TimeSpan.FromSeconds(30))
                {
                    _running = false;
                    return;
                }
            }
            await Wake.WaitAsync(PollEvery); // a change notification from Windows ends the wait early
        }
    }

    private static readonly SemaphoreSlim Wake = new(0, 1);

    /// <summary>
    /// What to show when the media session has disappeared: some players drop it briefly between tracks, and a
    /// single query occasionally fails. Keep the last track (paused) for <see cref="HoldLastFor"/>, then nothing.
    /// </summary>
    internal static MediaInfo? Hold(MediaInfo last, DateTime utcNow)
    {
        if (utcNow - _lastMediaUtc >= HoldLastFor)
            return null;
        return last.Playing ? last with { Playing = false, Position = last.PositionNow(utcNow), PositionAtUtc = utcNow } : last;
    }

    /// <summary>Diagnostics: raised (on a Windows thread) for each media change notification.</summary>
    public static event Action<string>? WindowsEvent;

    /// <summary>Re-check now instead of at the next poll (called when Windows reports a media change).</summary>
    internal static void Poke()
    {
        try { Wake.Release(); }
        catch (SemaphoreFullException) { } // already pending
    }

    /// <summary>Friendly name from an app id: "Spotify.exe" → "Spotify", "Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic" → "Media Player".</summary>
    public static string AppName(string? appId)
    {
        if (string.IsNullOrWhiteSpace(appId)) return "";
        string id = appId.Trim();
        if (id.Contains("ZuneMusic", StringComparison.OrdinalIgnoreCase)) return "Media Player";
        if (id.Contains("ZuneVideo", StringComparison.OrdinalIgnoreCase)) return "Films & TV";
        if (id.StartsWith("MSEdge", StringComparison.OrdinalIgnoreCase) || id.Contains("Microsoft.MicrosoftEdge", StringComparison.OrdinalIgnoreCase)) return "Edge";
        int bang = id.IndexOf('!');
        if (bang >= 0) id = id[(bang + 1)..];                    // packaged app: "Family_hash!AppId"
        id = Path.GetFileName(id.Replace('/', '\\'));
        if (id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) id = id[..^4];
        static bool Hashed(string s) => s.Length >= 10 && s.All(ch => char.IsAsciiDigit(ch) || char.IsAsciiLetterUpper(ch));
        int dot = id.LastIndexOf('.');
        if (dot > 0 && dot < id.Length - 1)
            id = Hashed(id[(dot + 1)..]) ? id[..dot]               // "Brave.YKWPBHOUVG…" → "Brave"
                                         : id[(dot + 1)..];        // "SpotifyAB.SpotifyMusic" → "SpotifyMusic"
        if (Hashed(id))
            return "";
        return id.Length == 0 ? "" : char.ToUpper(id[0]) + id[1..];
    }

    /// <summary>Diagnostics: one line describing what Windows reports (same subscribed manager the widget uses).</summary>
    public static async Task<string> DescribeAsync(CancellationToken ct)
    {
        try
        {
            var mgr = await WindowsMedia.ManagerAsync(ct);
            var parts = new List<string>();
            foreach (var session in mgr.GetSessions())
            {
                var props = await session.TryGetMediaPropertiesAsync().AsTask(ct);
                parts.Add($"{session.SourceAppUserModelId} '{props?.Title}' {session.GetPlaybackInfo()?.PlaybackStatus}");
            }
            var cur = mgr.GetCurrentSession();
            return $"sessions={parts.Count} [{string.Join("; ", parts)}] current={cur?.SourceAppUserModelId ?? "none"}";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    /// <summary>The real Windows source. The thumbnail is only fetched when the track changes.</summary>
    internal static class WindowsMedia
    {
        private static Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager? _manager;
        private static Windows.Media.Control.GlobalSystemMediaTransportControlsSession? _watched;
        private static string? _artKey;
        private static SKImage? _art;

        /// <summary>
        /// The session manager, subscribed to its change events. Windows only keeps a manager's view of sessions and
        /// track info fresh for listeners: without these handlers some players' changes (e.g. WACUP's) never show up.
        /// </summary>
        internal static async Task<Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager> ManagerAsync(CancellationToken ct)
        {
            if (_manager is { } m)
                return m;
            m = await Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(ct);
            m.SessionsChanged += OnSessionsChanged;
            m.CurrentSessionChanged += OnCurrentSessionChanged;
            return _manager = m;
        }

        private static void OnSessionsChanged(Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager s, Windows.Media.Control.SessionsChangedEventArgs e) => Changed("sessions changed");
        private static void OnCurrentSessionChanged(Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager s, Windows.Media.Control.CurrentSessionChangedEventArgs e) => Changed("current session changed");
        private static void OnMediaChanged(Windows.Media.Control.GlobalSystemMediaTransportControlsSession s, Windows.Media.Control.MediaPropertiesChangedEventArgs e) => Changed("track info changed");
        private static void OnPlaybackChanged(Windows.Media.Control.GlobalSystemMediaTransportControlsSession s, Windows.Media.Control.PlaybackInfoChangedEventArgs e) => Changed("playback changed");
        private static void OnTimelineChanged(Windows.Media.Control.GlobalSystemMediaTransportControlsSession s, Windows.Media.Control.TimelinePropertiesChangedEventArgs e) => Changed("position changed");

        private static void Changed(string what)
        {
            WindowsEvent?.Invoke(what);
            Poke();
        }

        /// <summary>
        /// Windows' "current" session can be empty while a player is clearly loaded (WACUP reports "Stopped" and
        /// briefly swaps sessions on track changes, and Windows then stops treating it as current). Pick one
        /// ourselves: a playing session first, then the one we were showing, then any with a title.
        /// </summary>
        private static async Task<Windows.Media.Control.GlobalSystemMediaTransportControlsSession?> PickSessionAsync(
            Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager manager, CancellationToken ct)
        {
            Windows.Media.Control.GlobalSystemMediaTransportControlsSession? previous = null, titled = null;
            string? previousApp = _artKey?.Split('|')[0];
            foreach (var session in manager.GetSessions())
            {
                if (session.GetPlaybackInfo()?.PlaybackStatus == Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    return session;
                if (previous is null && session.SourceAppUserModelId == previousApp)
                    previous = session;
                if (titled is null && !string.IsNullOrWhiteSpace((await session.TryGetMediaPropertiesAsync().AsTask(ct))?.Title))
                    titled = session;
            }
            return previous ?? titled;
        }

        /// <summary>Listens to the current session's own change events (and stops listening to the previous one).</summary>
        private static void Watch(Windows.Media.Control.GlobalSystemMediaTransportControlsSession? session)
        {
            if (ReferenceEquals(session, _watched))
                return;
            if (_watched is { } old)
            {
                try
                {
                    old.MediaPropertiesChanged -= OnMediaChanged;
                    old.PlaybackInfoChanged -= OnPlaybackChanged;
                    old.TimelinePropertiesChanged -= OnTimelineChanged;
                }
                catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ObjectDisposedException) { } // app already gone
            }
            _watched = session;
            if (session is not null)
            {
                session.MediaPropertiesChanged += OnMediaChanged;
                session.PlaybackInfoChanged += OnPlaybackChanged;
                session.TimelinePropertiesChanged += OnTimelineChanged;
            }
        }

        /// <summary>WACUP/Winamp as last seen through Windows' media session: supplies album art and exact tags.</summary>
        private static MediaInfo? _winampSmtc;

        /// <summary>
        /// Windows' media session, combined with the Winamp API for WACUP/Winamp: WACUP withdraws its session a few
        /// seconds into most tracks (and reports "Stopped" while playing), so its window is the reliable source.
        /// Another app that is actually playing wins over a paused Winamp.
        /// </summary>
        public static async Task<MediaInfo?> QueryAsync(CancellationToken ct)
        {
            MediaInfo? smtc;
            var wa = WinampSource.Read();
            try
            {
                smtc = await QuerySmtcAsync(ct);
            }
            catch (Exception) when (wa is { Status: WinampStatus.Playing or WinampStatus.Paused })
            {
                // Windows rebuilds a session now and then (e.g. when its media card opens) and a read can fail midway.
                // WACUP's own data is still good, so don't flash "unavailable".
                smtc = null;
            }
            Stage("combining with WACUP/Winamp");
            if (smtc is not null && WinampSource.IsWinampApp(smtc.AppId))
                _winampSmtc = smtc;

            if (wa is null || (smtc is { Playing: true } && !WinampSource.IsWinampApp(smtc.AppId)))
                return smtc;
            if (wa.Status is not (WinampStatus.Playing or WinampStatus.Paused))
                return smtc is not null && !WinampSource.IsWinampApp(smtc.AppId) ? smtc : null;
            return FromWinamp(wa, _winampSmtc, DateTime.UtcNow);
        }

        internal static MediaInfo FromWinamp(WinampTrack wa, MediaInfo? seen, DateTime utcNow)
        {
            var (artist, title) = WinampSource.SplitDisplay(wa.Display);
            // Same track as the media session last showed? Then use its tags and album art.
            bool same = seen is not null && seen.Title.Length > 0 && wa.Display.Contains(seen.Title, StringComparison.OrdinalIgnoreCase);
            string app = wa.ExeName.StartsWith("wacup", StringComparison.OrdinalIgnoreCase) ? "WACUP" : AppName(wa.ExeName);
            return new MediaInfo(same ? seen!.Title : title, same ? seen!.Artist : artist, same ? seen!.Album : "", app,
                wa.Status == WinampStatus.Playing, wa.Position, wa.Length, utcNow, same ? seen!.Art : null, wa.ExeName);
        }

        private static async Task<MediaInfo?> QuerySmtcAsync(CancellationToken ct)
        {
            Stage("connecting to Windows media");
            var manager = await ManagerAsync(ct);
            Stage("finding the media session");
            var session = manager.GetCurrentSession() ?? await PickSessionAsync(manager, ct);
            Stage("subscribing to the session");
            Watch(session);
            Stage("reading track info");
            if (session is null)
                return null;

            var props = await session.TryGetMediaPropertiesAsync().AsTask(ct);
            if (props is null || string.IsNullOrWhiteSpace(props.Title))
                return null;
            Stage("reading playback state");
            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();
            bool playing = playback?.PlaybackStatus == Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            var duration = timeline is null ? TimeSpan.Zero : timeline.EndTime - timeline.StartTime;
            var position = timeline is null ? TimeSpan.Zero : timeline.Position - timeline.StartTime;
            var at = timeline is null || timeline.LastUpdatedTime.Year < 2000 ? DateTime.UtcNow : timeline.LastUpdatedTime.UtcDateTime;
            if (duration < TimeSpan.FromSeconds(1)) duration = TimeSpan.Zero; // streams / apps that don't report a timeline

            string artist = string.IsNullOrWhiteSpace(props.Artist) ? props.AlbumArtist ?? "" : props.Artist;
            string key = $"{session.SourceAppUserModelId}|{props.Title}|{artist}|{props.AlbumTitle}";
            if (key != _artKey)
            {
                // Not disposed: the panel or the settings preview may still be drawing the previous image.
                // It's small (≤160 px); the GC finalizer frees it once nothing references it.
                _artKey = key;
                Stage("loading album art");
                _art = await LoadArtAsync(props.Thumbnail, ct);
            }

            return new MediaInfo(props.Title.Trim(), artist.Trim(), props.AlbumTitle?.Trim() ?? "", AppName(session.SourceAppUserModelId),
                playing, position, duration, at, _art, session.SourceAppUserModelId ?? "");
        }

        private static async Task<SKImage?> LoadArtAsync(Windows.Storage.Streams.IRandomAccessStreamReference? thumb, CancellationToken ct)
        {
            if (thumb is null)
                return null;
            try
            {
                using var winStream = await thumb.OpenReadAsync().AsTask(ct);
                using var stream = winStream.AsStreamForRead();
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms, ct);
                using var decoded = SKBitmap.Decode(ms.ToArray());
                if (decoded is null)
                    return null;

                float k = Math.Min(1f, 160f / Math.Max(decoded.Width, decoded.Height));
                var info = new SKImageInfo(Math.Max(1, (int)(decoded.Width * k)), Math.Max(1, (int)(decoded.Height * k)), SKColorType.Rgba8888, SKAlphaType.Premul);
                using var small = new SKBitmap(info);
                using (var c = new SKCanvas(small))
                using (var img = SKImage.FromBitmap(decoded))
                    c.DrawImage(img, new SKRect(0, 0, info.Width, info.Height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                return SKImage.FromBitmap(small);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException and not OperationCanceledException)
            {
                return null; // art is a nice-to-have
            }
        }
    }
}
