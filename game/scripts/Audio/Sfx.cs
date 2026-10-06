using System;
using Godot;
using EconGame.App;

namespace EconGame.Audio;

/// <summary>Tiny synthesised UI sounds (no asset files): click, alert, success and failure.</summary>
public static class Sfx
{
    static AudioStreamPlayer? _player;
    static AudioStreamWav? _click, _alert, _ok, _bad;

    static AudioStreamWav Tone(float f0, float f1, float seconds, float vol = 0.35f)
    {
        int rate = 22050, n = (int)(rate * seconds); var data = new byte[n * 2];
        double ph = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n, f = Mathf.Lerp(f0, f1, t);
            ph += 2 * Math.PI * f / rate;
            float env = Mathf.Min(1f, t * 30f) * Mathf.Pow(1 - t, 2f);
            short v = (short)(Math.Sin(ph) * env * vol * short.MaxValue);
            data[2 * i] = (byte)(v & 0xFF); data[2 * i + 1] = (byte)((v >> 8) & 0xFF);
        }
        return new AudioStreamWav { Data = data, Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Stereo = false };
    }

    static void Play(ref AudioStreamWav? s, float f0, float f1, float dur, float vol = 0.35f)
    {
        if (Settings.Volume <= 0.001f || Main.Instance == null) return;
        s ??= Tone(f0, f1, dur, vol);
        if (_player == null) { _player = new AudioStreamPlayer { Bus = "Master" }; Main.Instance.AddChild(_player); }
        _player.Stream = s; _player.Play();
    }

    public static void Click() => Play(ref _click, 900, 620, 0.05f, 0.25f);
    public static void Alert() => Play(ref _alert, 520, 780, 0.22f);
    public static void Ok() => Play(ref _ok, 660, 990, 0.14f);
    public static void Bad() => Play(ref _bad, 300, 180, 0.2f);
}
