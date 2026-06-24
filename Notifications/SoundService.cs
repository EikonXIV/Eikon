using System.IO;
using System.Reflection;
using Dalamud.Plugin.Services;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Eikon.Notifications;

// Plays the bundled notification chime at a configurable volume. The WAV is embedded in the plugin
// assembly (stripped to fmt+data, no metadata). Each Play creates a short-lived WaveOutEvent on
// NAudio's own callback thread and disposes it when playback ends, so overlapping chimes are fine and
// nothing leaks. WinMM output uses its own device path, so this coexists with the game's audio. All
// failures (no device, decode error) are swallowed: a missing chime must never break notifications.
internal sealed class SoundService : IDisposable
{
    private const string ResourceName = "Eikon.message-chime.wav";

    private readonly IPluginLog log;
    private readonly byte[]? wav;
    private readonly object gate = new();
    private readonly List<IWavePlayer> active = new();
    private bool disposed;

    public SoundService(IPluginLog log)
    {
        this.log = log;
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (stream == null)
            {
                this.log.Warning("Notification sound resource not found: " + ResourceName);
                return;
            }

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            this.wav = ms.ToArray();
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "Loading the notification sound failed.");
        }
    }

    // volume is 0-100. A volume of 0 (or a missing chime) plays nothing.
    public void Play(int volume)
    {
        if (this.wav == null || volume <= 0)
            return;

        try
        {
            // WaveFileReader takes ownership of the stream and disposes it with the reader.
            var reader = new WaveFileReader(new MemoryStream(this.wav));
            var sample = new VolumeSampleProvider(reader.ToSampleProvider())
            {
                Volume = Math.Clamp(volume, 0, 100) / 100f,
            };
            var output = new WaveOutEvent();
            output.Init(sample);
            output.PlaybackStopped += (_, _) =>
            {
                lock (this.gate)
                    this.active.Remove(output);
                output.Dispose();
                reader.Dispose();
            };

            lock (this.gate)
            {
                if (this.disposed)
                {
                    output.Dispose();
                    reader.Dispose();
                    return;
                }

                this.active.Add(output);
            }

            output.Play();
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "Playing the notification sound failed.");
        }
    }

    public void Dispose()
    {
        lock (this.gate)
        {
            this.disposed = true;
            foreach (var output in this.active)
            {
                try { output.Stop(); output.Dispose(); }
                catch { /* best-effort teardown */ }
            }

            this.active.Clear();
        }
    }
}
