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

namespace S1Panel.Core.Services;

public sealed record SunDay(DateTime? Sunrise, DateTime? Sunset, bool AlwaysUp, bool AlwaysDown)
{
    public TimeSpan? DayLength => Sunrise is { } r && Sunset is { } s ? s - r : AlwaysUp ? TimeSpan.FromHours(24) : AlwaysDown ? TimeSpan.Zero : null;
}

public sealed record MoonInfo(double AgeDays, double Phase, double Illumination, string Name, bool Waxing, DateTime NextFullUtc, DateTime NextNewUtc);

/// <summary>
/// Sunrise/sunset and moon phase, computed locally (no internet). Sun: the standard "sunrise equation"
/// (NOAA-style, ±~1 minute at mid latitudes). Moon: mean synodic month from a reference new moon (±~half a day).
/// </summary>
public static class Astronomy
{
    private const double Deg = Math.PI / 180;
    private const double SynodicMonth = 29.530588853;
    private static readonly DateTime ReferenceNewMoonUtc = new(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);

    /// <summary>Sunrise and sunset for a date, converted to <paramref name="zone"/> (default: this PC's time zone).</summary>
    public static SunDay Sun(double latitude, double longitude, DateOnly date, TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        double jdNoon = date.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc).ToOADate() + 2415018.5;
        double n = Math.Round(jdNoon - 2451545.0 + 0.0008);
        double jStar = n - longitude / 360.0;
        double m = Mod(357.5291 + 0.98560028 * jStar, 360);
        double c = 1.9148 * Math.Sin(m * Deg) + 0.0200 * Math.Sin(2 * m * Deg) + 0.0003 * Math.Sin(3 * m * Deg);
        double lambda = Mod(m + c + 180 + 102.9372, 360);
        double jTransit = 2451545.0 + jStar + 0.0053 * Math.Sin(m * Deg) - 0.0069 * Math.Sin(2 * lambda * Deg);
        double sinDec = Math.Sin(lambda * Deg) * Math.Sin(23.4397 * Deg);
        double cosDec = Math.Cos(Math.Asin(sinDec));
        double cosH = (Math.Sin(-0.833 * Deg) - Math.Sin(latitude * Deg) * sinDec) / (Math.Cos(latitude * Deg) * cosDec);

        if (cosH < -1) return new SunDay(null, null, AlwaysUp: true, AlwaysDown: false);
        if (cosH > 1) return new SunDay(null, null, AlwaysUp: false, AlwaysDown: true);

        double h = Math.Acos(cosH) / Deg;
        DateTime ToLocal(double jd) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(DateTime.FromOADate(jd - 2415018.5), DateTimeKind.Utc), zone);
        return new SunDay(ToLocal(jTransit - h / 360), ToLocal(jTransit + h / 360), false, false);
    }

    public static MoonInfo Moon(DateTime utc)
    {
        double age = Mod((utc - ReferenceNewMoonUtc).TotalDays, SynodicMonth);
        double phase = age / SynodicMonth; // 0 new, 0.25 first quarter, 0.5 full, 0.75 last quarter
        double illumination = (1 - Math.Cos(2 * Math.PI * phase)) / 2;
        string name = ((int)Math.Floor(phase * 8 + 0.5) % 8) switch
        {
            0 => "New moon",
            1 => "Waxing crescent",
            2 => "First quarter",
            3 => "Waxing gibbous",
            4 => "Full moon",
            5 => "Waning gibbous",
            6 => "Last quarter",
            _ => "Waning crescent",
        };
        double toFull = Mod(SynodicMonth / 2 - age, SynodicMonth);
        double toNew = SynodicMonth - age;
        return new MoonInfo(age, phase, illumination, name, phase < 0.5, utc.AddDays(toFull), utc.AddDays(toNew));
    }

    private static double Mod(double a, double m) => ((a % m) + m) % m;
}
