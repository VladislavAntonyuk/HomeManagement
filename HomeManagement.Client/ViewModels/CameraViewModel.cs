using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HomeManagement.Client.Services;

namespace HomeManagement.Client.ViewModels;

public sealed record StreamQuality(string Name, int Height, int Fps)
{
    public override string ToString() => Name;
}

public sealed partial class CameraViewModel(ICameraStreamApi streamApi) : ObservableObject, IQueryAttributable
{
    public const string CameraQueryKey = "camera";

    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(3);

    private CancellationTokenSource? _streamCts;

    public StreamQuality[] Qualities { get; } =
    [
        new(AppResources.QualityLow, 360, 5),
        new(AppResources.QualityMedium, 720, 10),
        new(AppResources.QualityHigh, 1080, 15)
    ];

    [ObservableProperty]
    public partial string Camera { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial ImageSource? Frame { get; private set; }

    [ObservableProperty]
    public partial StreamQuality SelectedQuality { get; set; } = null!;

    [ObservableProperty]
    public partial string? Status { get; private set; } = AppResources.Connecting;

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
                Status = Frame is null ? AppResources.Connecting : AppResources.Reconnecting;
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
                        Fps = string.Format(CultureInfo.CurrentCulture, AppResources.FramesPerSecond, frames / watch.Elapsed.TotalSeconds);
                        frames = 0;
                        watch.Restart();
                    }
                }

                Status = AppResources.StreamEnded;
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
