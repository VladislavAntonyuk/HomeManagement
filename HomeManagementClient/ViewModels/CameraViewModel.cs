using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HomeManagementClient.Services;

namespace HomeManagementClient.ViewModels;

public sealed record StreamQuality(string Name, int Height, int Fps)
{
    public override string ToString() => Name;
}

/// <summary>
/// Live view of one camera, played from the MJPEG stream the hub proxies from Frigate.
/// </summary>
public sealed partial class CameraViewModel(ICameraStreamApi streamApi) : ObservableObject, IQueryAttributable
{
    public const string CameraQueryKey = "camera";

    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(3);

    private CancellationTokenSource? _streamCts;

    public StreamQuality[] Qualities { get; } =
    [
        new("Low · 360p", 360, 5),
        new("Medium · 720p", 720, 10),
        new("High · 1080p", 1080, 15)
    ];

    [ObservableProperty]
    public partial string Camera { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial ImageSource? Frame { get; private set; }

    [ObservableProperty]
    public partial StreamQuality SelectedQuality { get; set; } = null!;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    public partial string? Status { get; private set; } = "Connecting...";

    public bool HasStatus => Status is not null;

    [ObservableProperty]
    public partial string Fps { get; private set; } = string.Empty;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        Camera = query.TryGetValue(CameraQueryKey, out var camera) ? camera as string ?? string.Empty : string.Empty;
        SelectedQuality ??= Qualities[1];
    }

    [RelayCommand]
    private void Start()
    {
        Stop();
        _streamCts = new CancellationTokenSource();
        _ = PlayAsync(Camera, SelectedQuality, _streamCts.Token);
    }

    [RelayCommand]
    private void Stop()
    {
        _streamCts?.Cancel();
        _streamCts?.Dispose();
        _streamCts = null;
    }

    partial void OnSelectedQualityChanged(StreamQuality oldValue, StreamQuality newValue)
    {
        if (oldValue is not null && _streamCts is not null)
        {
            Start();
        }
    }

    private async Task PlayAsync(string camera, StreamQuality quality, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                Status = Frame is null ? "Connecting..." : "Reconnecting...";
                await using var stream = await streamApi.GetCameraStreamAsync(camera, quality.Height, quality.Fps, token);

                var frames = 0;
                var watch = Stopwatch.StartNew();
                await foreach (var frame in MjpegReader.ReadFramesAsync(stream, token))
                {
                    Frame = ImageSource.FromStream(() => new MemoryStream(frame));
                    Status = null;

                    frames++;
                    if (watch.Elapsed >= TimeSpan.FromSeconds(1))
                    {
                        Fps = $"{frames / watch.Elapsed.TotalSeconds:0.#} fps";
                        frames = 0;
                        watch.Restart();
                    }
                }

                Status = "Stream ended.";
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                Status = ApiErrors.ToMessage(e);
            }

            Fps = string.Empty;
            try
            {
                await Task.Delay(ReconnectDelay, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
