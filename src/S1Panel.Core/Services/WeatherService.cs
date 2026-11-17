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
using System.Text.Json;

namespace S1Panel.Core.Services;

public enum WeatherKind { Clear, PartlyCloudy, Cloudy, Fog, Drizzle, Rain, Showers, Snow, Thunder }

public sealed record WeatherDay(DateOnly Date, int Code, double MaxC, double MinC, int? PrecipitationChance);

public sealed record WeatherData(
    double TempC, double FeelsLikeC, int Code, bool IsDay, int Humidity, double WindKmh,
    IReadOnlyList<WeatherDay> Days, DateTime FetchedUtc);

public sealed record WeatherState(WeatherData? Data, string? Problem);

/// <summary>
/// Current conditions + a short forecast from Open-Meteo (free, no API key). Fetched in the background every
/// 15 minutes and cached per location; the last good data is kept if a refresh fails. Only the coordinates are sent.
/// </summary>
public static class WeatherService
{
    private static readonly HttpClient Http = LocationService.CreateHttp();
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Entry> Cache = [];

    public static TimeSpan RefreshEvery { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Replaceable for tests: returns the raw JSON for a location.</summary>
    public static Func<GeoLocation, CancellationToken, Task<string>> Fetcher { get; set; } = FetchAsync;

    private sealed class Entry
    {
        public WeatherData? Data;
        public string? Problem;
        public DateTime AttemptUtc;
        public bool Busy;
    }

    /// <summary>Latest weather for a location; starts a background refresh when due. Never blocks.</summary>
    public static WeatherState Get(GeoLocation loc)
    {
        string key = string.Create(CultureInfo.InvariantCulture, $"{loc.Latitude:0.00},{loc.Longitude:0.00}");
        lock (Gate)
        {
            if (!Cache.TryGetValue(key, out var e))
                Cache[key] = e = new Entry();

            var retryAfter = e.Problem is null ? RefreshEvery : TimeSpan.FromMinutes(3);
            if (!e.Busy && DateTime.UtcNow - e.AttemptUtc > retryAfter)
            {
                e.Busy = true;
                e.AttemptUtc = DateTime.UtcNow;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var data = Parse(await Fetcher(loc, CancellationToken.None), DateTime.UtcNow);
                        lock (Gate) { e.Data = data; e.Problem = null; }
                    }
                    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
                    {
                        lock (Gate) e.Problem = ex is HttpRequestException or TaskCanceledException ? "No connection to the weather service" : "Unexpected weather data";
                    }
                    finally
                    {
                        lock (Gate) e.Busy = false;
                    }
                });
            }
            return new WeatherState(e.Data, e.Problem);
        }
    }

    private static Task<string> FetchAsync(GeoLocation loc, CancellationToken ct)
    {
        string url = FormattableString.Invariant($"https://api.open-meteo.com/v1/forecast?latitude={loc.Latitude:0.####}&longitude={loc.Longitude:0.####}")
            + "&current=temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,is_day,wind_speed_10m"
            + "&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max"
            + "&timezone=auto&forecast_days=4";
        return Http.GetStringAsync(url, ct);
    }

    public static WeatherData Parse(string json, DateTime fetchedUtc)
    {
        using var doc = JsonDocument.Parse(json);
        var cur = doc.RootElement.GetProperty("current");
        var daily = doc.RootElement.GetProperty("daily");

        var dates = daily.GetProperty("time").EnumerateArray().Select(d => DateOnly.Parse(d.GetString()!, CultureInfo.InvariantCulture)).ToArray();
        var codes = daily.GetProperty("weather_code").EnumerateArray().Select(x => x.GetInt32()).ToArray();
        var max = daily.GetProperty("temperature_2m_max").EnumerateArray().Select(x => x.GetDouble()).ToArray();
        var min = daily.GetProperty("temperature_2m_min").EnumerateArray().Select(x => x.GetDouble()).ToArray();
        var rain = daily.TryGetProperty("precipitation_probability_max", out var p)
            ? p.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Number ? x.GetInt32() : (int?)null).ToArray()
            : new int?[dates.Length];

        var days = dates.Select((d, i) => new WeatherDay(d, codes[i], max[i], min[i], rain.ElementAtOrDefault(i))).ToList();
        return new WeatherData(
            cur.GetProperty("temperature_2m").GetDouble(),
            cur.GetProperty("apparent_temperature").GetDouble(),
            cur.GetProperty("weather_code").GetInt32(),
            cur.GetProperty("is_day").GetInt32() == 1,
            cur.GetProperty("relative_humidity_2m").GetInt32(),
            cur.GetProperty("wind_speed_10m").GetDouble(),
            days,
            fetchedUtc);
    }

    /// <summary>WMO weather code → icon kind and a short description.</summary>
    public static (WeatherKind Kind, string Text) Describe(int code) => code switch
    {
        0 => (WeatherKind.Clear, "Clear"),
        1 => (WeatherKind.Clear, "Mostly clear"),
        2 => (WeatherKind.PartlyCloudy, "Partly cloudy"),
        3 => (WeatherKind.Cloudy, "Overcast"),
        45 or 48 => (WeatherKind.Fog, "Fog"),
        51 or 53 or 55 => (WeatherKind.Drizzle, "Drizzle"),
        56 or 57 => (WeatherKind.Drizzle, "Freezing drizzle"),
        61 => (WeatherKind.Rain, "Light rain"),
        63 => (WeatherKind.Rain, "Rain"),
        65 => (WeatherKind.Rain, "Heavy rain"),
        66 or 67 => (WeatherKind.Rain, "Freezing rain"),
        71 => (WeatherKind.Snow, "Light snow"),
        73 => (WeatherKind.Snow, "Snow"),
        75 => (WeatherKind.Snow, "Heavy snow"),
        77 => (WeatherKind.Snow, "Snow grains"),
        80 or 81 => (WeatherKind.Showers, "Showers"),
        82 => (WeatherKind.Showers, "Heavy showers"),
        85 or 86 => (WeatherKind.Snow, "Snow showers"),
        95 => (WeatherKind.Thunder, "Thunderstorm"),
        96 or 99 => (WeatherKind.Thunder, "Thunderstorm, hail"),
        _ => (WeatherKind.Cloudy, "Unknown"),
    };
}
