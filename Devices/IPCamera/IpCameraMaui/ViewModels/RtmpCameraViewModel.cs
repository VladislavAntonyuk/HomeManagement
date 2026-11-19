using System.Collections.ObjectModel;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IpCameraMaui.Pages;

namespace IpCameraMaui.ViewModels;

#if ANDROID
using Android.Views;
using Com.Pedro.Encoder.Input.Video;
using Com.Pedro.Rtplibrary.Rtmp;
#endif

public partial class RtmpCameraViewModel(SettingsViewModel settingsViewModel) : ObservableObject
{
    [ObservableProperty]
    public partial bool IsStreaming { get; private set; }

    public ObservableCollection<string> Logs { get; set; } = [];
    public ObservableCollection<Size> Resolutions { get; set; } = [];

    [ObservableProperty]
    public partial Size SelectedResolution { get; set; } = new (1920, 1080);

    [ObservableProperty]
    public partial int Bitrate { get; set; } = 5000;

#if ANDROID
    private RtmpCamera1? _rtmpCamera;

    public async Task Init(SurfaceView surfaceView)
    {
        var cameraRequest = await Permissions.RequestAsync<Permissions.Camera>();
        var microphoneRequest = await Permissions.RequestAsync<Permissions.Microphone>();
        if (cameraRequest != PermissionStatus.Granted || microphoneRequest != PermissionStatus.Granted)
        {
            await Shell.Current.CurrentPage.DisplayAlertAsync("Permission Denied", "Camera and Microphone permissions are required to use this feature.", "OK");
            return;
        }


        _rtmpCamera = new RtmpCamera1(surfaceView, new ConnectChecker(s => Logs.Add($"{DateTime.Now} - {s}")));
        Resolutions.Clear();
        // RtmpCamera1 is built on the Camera1 API, so its resolutions are the obsolete Camera.Size.
#pragma warning disable CS0618
        foreach (var resolution in (_rtmpCamera.ResolutionsBack ?? []).Select(x => new Size(x.Width, x.Height)))
#pragma warning restore CS0618
        {
            Resolutions.Add(resolution);
        }

        SelectedResolution = Resolutions.FirstOrDefault(new Size(1280, 720));

        IsPowerSavingModeEnabled = settingsViewModel.IsPowerSavingModeEnabled;
    }
#endif

    public async Task StartRtmpStream()
    {
        Logs.Clear();
#if ANDROID
        if (_rtmpCamera is null)
        {
            return;
        }

        if (_rtmpCamera.PrepareAudio() && _rtmpCamera.PrepareVideo((int)SelectedResolution.Width, (int)SelectedResolution.Height, Bitrate * 1024))
        {
            DeviceDisplay.KeepScreenOn = true;
            if (string.IsNullOrWhiteSpace(settingsViewModel.RecordingsFolder) || !settingsViewModel.SaveRecordingToFileStorage)
            {
                _rtmpCamera.StartStream(settingsViewModel.RtmpAddressText);
            }
            else
            {
                await DeleteOldRecords();
                _rtmpCamera.StartStreamAndRecord(settingsViewModel.RtmpAddressText, Path.Combine(settingsViewModel.RecordingsFolder, $"recording-{DateTimeOffset.Now.ToUnixTimeMilliseconds()}.mp4"));
            }

            if (_rtmpCamera.CameraFacing != CameraHelper.Facing.Back)
            {
                _rtmpCamera.SwitchCamera();
            }

            IsStreaming = true;
        }
#endif
    }

    [RelayCommand]
    private void StopRtmpStream()
    {
#if ANDROID
        if (_rtmpCamera is null)
        {
            return;
        }

        if (_rtmpCamera.IsStreaming)
        {
            _rtmpCamera.StopStream();
        }

        if (_rtmpCamera.IsRecording)
        {
            _rtmpCamera.StopRecord();
        }

        IsStreaming = false;
        DeviceDisplay.KeepScreenOn = false;
#endif
    }



    [RelayCommand(AllowConcurrentExecutions = false)]
    async Task OpenSettings()
    {
        var popup = new SettingsPage(settingsViewModel);
        await Shell.Current.ShowPopupAsync(popup, new PopupOptions() { CanBeDismissedByTappingOutsideOfPopup = true });
    }

    [ObservableProperty]
    public partial bool IsPowerSavingModeEnabled { get; set; }


    [RelayCommand]
    void DisablePowerSavingMode()
    {
        IsPowerSavingModeEnabled = false;
    }

    async Task DeleteOldRecords()
    {
        if (string.IsNullOrWhiteSpace(settingsViewModel.RecordingsFolder) || !settingsViewModel.SaveRecordingToFileStorage)
        {
            return;
        }

        var files = Directory.GetFiles(settingsViewModel.RecordingsFolder, "*.mp4");
        if (files.Length >= settingsViewModel.MaxFiles)
        {
            var filesToDelete = files
                .OrderByDescending(f => new FileInfo(f).CreationTime)
                .Skip(settingsViewModel.MaxFiles - 1)
                .ToList();
            foreach (var file in filesToDelete)
            {
                File.Delete(file);
            }
        }
    }

    async partial void OnSelectedResolutionChanged(Size value)
    {
#if ANDROID
        if (_rtmpCamera is not null)
        {
            StopRtmpStream();
            await StartRtmpStream();
        }
#endif
    }

    async partial void OnBitrateChanged(int value)
    {
#if ANDROID
        if (_rtmpCamera is not null)
        {
            StopRtmpStream();
            await StartRtmpStream();
        }
#endif
    }
}