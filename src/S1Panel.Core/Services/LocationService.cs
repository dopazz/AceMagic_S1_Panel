// S1 Panel - front LCD and LED strip control for the AceMagic S1
// Copyright (C) 2026 dopaz
// SPDX-License-Identifier: GPL-3.0-or-later
//
// This program is free software: you can redistribute it and/or modify it under the terms of the
// GNU General Public License as published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version. It is distributed WITHOUT ANY WARRANTY; see the
// GNU General Public License in LICENSE for details.
//
// Developed with Claude Code (Claude Opus 5.5).

using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Windows.Devices.Geolocation;

namespace S1Panel.Core.Services;

public sealed record GeoLocation(double Latitude, double Longitude, string? Name, string Source)
{
    public override string ToString() =>
        $"{Name ?? "(unnamed)"} ({Latitude.ToString("0.###", CultureInfo.InvariantCulture)}, {Longitude.ToString("0.###", CultureInfo.InvariantCulture)}) via {Source}";
}

public enum LocationStatus { Resolving, Ready, NeedsInput, Failed }

public sealed record LocationState(LocationStatus Status, GeoLocation? Location, string Message);

/// <summary>
/// Resolves the "location" setting to coordinates, in the background, cached per setting value:
///   "auto"          → Windows location service (Settings → Privacy &amp; security → Location). No admin needed.
///   "41.88,-87.63"  → used as is.
///   anything else   → a ZIP/postal code or place name, looked up with Open-Meteo's free geocoding API.
/// When "auto" can't get a position, <see cref="WindowsLocationUnavailable"/> fires so a UI can ask for a ZIP code.
/// </summary>
public static class LocationService
{
    private static readonly HttpClient Http = CreateHttp();
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Entry> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised (on a background thread) when "auto" can't get a position from Windows. Argument: why.</summary>
    public static event Action<string>? WindowsLocationUnavailable;

    /// <summary>Replaceable for tests.</summary>
    public static Func<CancellationToken, Task<(GeoLocation? Location, string Problem)>> WindowsLocator { get; set; } = FromWindowsAsync;

    /// <summary>Replaceable for tests.</summary>
    public static Func<string, CancellationToken, Task<GeoLocation?>> Geocoder { get; set; } = GeocodeAsync;

    private sealed class Entry
    {
        public LocationState State = new(LocationStatus.Resolving, null, "Locating…");
        public DateTime ResolvedUtc;
        public bool Busy;
    }

    /// <summary>Current state for a setting value; starts or refreshes a background lookup when needed. Never blocks.</summary>
    public static LocationState Get(string setting)
    {
        string key = Normalize(setting);
        lock (Gate)
        {
            if (!Cache.TryGetValue(key, out var e))
                Cache[key] = e = new Entry();

            var age = DateTime.UtcNow - e.ResolvedUtc;
            bool due = e.State.Status switch
            {
                LocationStatus.Resolving => e.ResolvedUtc == default,
                LocationStatus.Ready => key == "auto" && age > TimeSpan.FromHours(6),
                _ => age > TimeSpan.FromMinutes(10), // retry failures and "needs input" now and then
            };
            if (due && !e.Busy)
            {
                e.Busy = true;
                _ = Task.Run(async () =>
                {
                    var state = await ResolveAsync(key, CancellationToken.None);
                    lock (Gate)
                    {
                        // keep a previously good position if a refresh fails
                        if (state.Status == LocationStatus.Ready || e.State.Status != LocationStatus.Ready)
                            e.State = state;
                        e.ResolvedUtc = DateTime.UtcNow;
                        e.Busy = false;
                    }
                });
            }
            return e.State;
        }
    }

    /// <summary>Resolves now (used by the CLI's "location" command and the settings window's Detect button).</summary>
    public static async Task<LocationState> ResolveAsync(string setting, CancellationToken ct)
    {
        string s = Normalize(setting);
        try
        {
            if (s == "auto")
            {
                var (loc, problem) = await WindowsLocator(ct);
                if (loc is not null)
                    return new(LocationStatus.Ready, loc, loc.ToString());
                WindowsLocationUnavailable?.Invoke(problem);
                return new(LocationStatus.NeedsInput, null, $"{problem} Enter a ZIP code in Settings.");
            }

            if (TryParseCoordinates(s, out var coords))
                return new(LocationStatus.Ready, coords, coords.ToString());

            var found = await Geocoder(s, ct);
            return found is null
                ? new(LocationStatus.Failed, null, $"Couldn't find '{s}'.")
                : new(LocationStatus.Ready, found, found.ToString());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new(LocationStatus.Failed, null, $"Location lookup failed ({ex.Message.Split('\n')[0]}).");
        }
    }

    public static bool TryParseCoordinates(string s, out GeoLocation location)
    {
        location = null!;
        string[] parts = s.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon)
            && lat is >= -90 and <= 90 && lon is >= -180 and <= 180)
        {
            location = new GeoLocation(lat, lon, null, "settings");
            return true;
        }
        return false;
    }

    private static string Normalize(string? setting) => string.IsNullOrWhiteSpace(setting) ? "auto" : setting.Trim();

    private static async Task<(GeoLocation?, string)> FromWindowsAsync(CancellationToken ct)
    {
        try
        {
            var access = await Geolocator.RequestAccessAsync();
            if (access != GeolocationAccessStatus.Allowed)
                return (null, "Windows location access is off for desktop apps (Settings → Privacy & security → Location).");

            var locator = new Geolocator { DesiredAccuracy = PositionAccuracy.Default };
            var fix = locator.GetGeopositionAsync(TimeSpan.FromHours(1), TimeSpan.FromSeconds(20)).AsTask(ct);
            var pos = await fix.WaitAsync(TimeSpan.FromSeconds(30), ct);
            var p = pos.Coordinate.Point.Position;
            return (new GeoLocation(p.Latitude, p.Longitude, null, "Windows location"), "");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException or TimeoutException or TaskCanceledException or InvalidOperationException)
        {
            return (null, $"Windows couldn't provide a location ({ex.Message.Split('\n')[0].Trim()}).");
        }
    }

    /// <summary>ZIP/postal code or place name → coordinates via Open-Meteo geocoding (no API key).</summary>
    private static async Task<GeoLocation?> GeocodeAsync(string query, CancellationToken ct)
    {
        bool postal = query.Any(char.IsDigit);
        string url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(query)}&count=1&language=en&format=json";
        if (postal)
            url += $"&countryCode={RegionInfo.CurrentRegion.TwoLetterISORegionName}";

        using var doc = await Http.GetFromJsonAsync<JsonDocument>(url, ct);
        if (doc is null || !doc.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
            return null;
        var r = results[0];
        string name = r.GetProperty("name").GetString() ?? query;
        if (r.TryGetProperty("admin1", out var admin) && admin.GetString() is { Length: > 0 } a)
            name += $", {a}";
        return new GeoLocation(r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble(), name, $"'{query}'");
    }

    internal static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("s1panel/1.0");
        return http;
    }
}
