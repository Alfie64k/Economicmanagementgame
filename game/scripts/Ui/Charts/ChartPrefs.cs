using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Sim.Core.Model;
using Sim.Core.Scoring;

namespace EconGame.Ui;

/// <summary>A dated annotation on a time-series chart: something you did or something that happened.</summary>
public sealed class ChartMarker
{
    public double X;            // months since the start of the game
    public string Label = "";
    public Color Color = Colors.White;
    public bool Mine;           // drawn as a downward triangle when yours, an upward one when it happened to you
}

/// <summary>How annotated charts look, shared by every chart that opts in: the time window and whether markers show.</summary>
public static class ChartPrefs
{
    /// <summary>Months of history shown, 0 for all of it.</summary>
    public static int Window;
    public static bool Markers = true;
    public static event Action? Changed;
    public static void Set(int window, bool markers)
    {
        if (window == Window && markers == Markers) return;
        Window = window; Markers = markers; Changed?.Invoke();
    }

    static int _cacheKey = -1; static string _cacheGame = ""; static List<ChartMarker> _cache = new();

    /// <summary>Markers for the running game, built from the journal (so from the command and news logs; nothing extra is stored).</summary>
    public static List<ChartMarker> For(World w)
    {
        int key = w.Month * 100000 + w.CommandLog.Count * 100 + w.Log.Count % 100;
        string id = w.PlayerId + w.Seed;
        if (key == _cacheKey && id == _cacheGame) return _cache;
        _cacheKey = key; _cacheGame = id;
        _cache = Journal.Build(w).Select(e => new ChartMarker
        {
            X = e.Month,
            Label = (e.Title.Length > 70 ? e.Title[..67] + "…" : e.Title),
            Mine = e.Kind is "action" or "policy" or "project",
            Color = e.Kind switch
            {
                "action" => Pal.Accent, "policy" => Pal.Good, "project" => Pal.Series[2],
                "crisis" => Pal.Bad, "election" => Pal.Series[6], "milestone" => Pal.Warn, _ => Pal.Warn,
            },
        }).ToList();
        return _cache;
    }
}
