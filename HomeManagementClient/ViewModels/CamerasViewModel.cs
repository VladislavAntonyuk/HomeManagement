using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HomeManagementClient.Services;

namespace HomeManagementClient.ViewModels;

public sealed partial class CamerasViewModel(IHomeManagementApi api, UserNotifier notifier, HubSettings settings)
    : RefreshableViewModel(notifier, settings)
{
    private const int SnapshotHeight = 360;

    [ObservableProperty]
    public partial IReadOnlyList<CameraItemViewModel> Cameras { get; private set; } = [];

    protected override async Task LoadAsync(CancellationToken token)
    {
        var cameras = await api.GetCamerasAsync(token);
        if (!cameras.Select(x => x.Name).SequenceEqual(Cameras.Select(x => x.Name)))
        {
            Cameras = cameras.Select(x => new CameraItemViewModel(x.Name)).ToList();
        }

        await Task.WhenAll(Cameras.Select(x => LoadSnapshotAsync(x, token)));
    }

    private async Task LoadSnapshotAsync(CameraItemViewModel camera, CancellationToken token)
    {
        try
        {
            await using var stream = await api.GetCameraSnapshotAsync(camera.Name, SnapshotHeight, token);
            var bytes = await ReadAllAsync(stream, token);
            camera.Snapshot = ImageSource.FromStream(() => new MemoryStream(bytes));
            camera.SnapshotError = null;
            camera.UpdatedAt = DateTimeOffset.Now;
        }
        catch (Exception e) when (e is not OperationCanceledException || !token.IsCancellationRequested)
        {
            camera.SnapshotError = ApiErrors.ToMessage(e);
        }
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream, CancellationToken token)
    {
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, token);
        return memory.ToArray();
    }

    [RelayCommand]
    private Task OpenCameraAsync(CameraItemViewModel camera)
        => Shell.Current.GoToAsync(Routes.Camera, new ShellNavigationQueryParameters { [CameraViewModel.CameraQueryKey] = camera.Name });
}

public sealed partial class CameraItemViewModel(string name) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    public partial ImageSource? Snapshot { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdatedText))]
    public partial string? SnapshotError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdatedText))]
    public partial DateTimeOffset? UpdatedAt { get; set; }

    public string UpdatedText => (UpdatedAt, SnapshotError) switch
    {
        (_, not null) => "Unavailable",
        ({ } value, _) => $"Snapshot {value:HH:mm:ss}",
        _ => "Loading..."
    };
}
