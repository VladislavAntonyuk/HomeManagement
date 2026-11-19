using CommunityToolkit.Maui.Storage;
using CommunityToolkit.Mvvm.ComponentModel;

namespace IpCameraMaui.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
	private readonly IAutoStartService _autoStartService;
	private readonly IPreferences _preferences;
	private bool _isFirstStart = true;

	public SettingsViewModel(IAutoStartService autoStartService, IPreferences preferences)
	{
		this._autoStartService = autoStartService;
		this._preferences = preferences;

		RtmpAddressText = preferences.Get(nameof(RtmpAddressText), "rtmp://localhost:1935/live/demo");
        MaxFiles = preferences.Get(nameof(MaxFiles), 1);
		IsAutoStartEnabled = preferences.Get(nameof(IsAutoStartEnabled), autoStartService.IsAutoStartEnabledAsync().GetAwaiter().GetResult());
		RecordingsFolder = preferences.Get<string?>(nameof(RecordingsFolder), null);
		if (!string.IsNullOrWhiteSpace(RecordingsFolder))
		{
			SaveRecordingToFileStorage = true;
		}
		else
		{
			_isFirstStart = false;
		}

        IsPowerSavingModeEnabled = preferences.Get(nameof(IsPowerSavingModeEnabled), false);
	}


	[ObservableProperty]
	public partial string RtmpAddressText { get; set; }

	[ObservableProperty]
	public partial int MaxFiles { get; set; }

	[ObservableProperty]
	public partial bool IsAutoStartEnabled { get; set; }

	[ObservableProperty]
	public partial bool SaveRecordingToFileStorage { get; set; }

	[ObservableProperty]
	public partial string? RecordingsFolder { get; set; }

	[ObservableProperty]
	public partial bool IsPowerSavingModeEnabled { get; set; }

    partial void OnRtmpAddressTextChanged(string value)
    {
        _preferences.Set(nameof(RtmpAddressText), value);
    }

    partial void OnMaxFilesChanged(int value)
    {
        _preferences.Set(nameof(MaxFiles), value);
    }

    async partial void OnIsAutoStartEnabledChanged(bool value)
	{
		if (value)
		{
			await _autoStartService.EnableAutoStartAsync();
		}
		else
		{
			await _autoStartService.DisableAutoStartAsync();
		}

		_preferences.Set(nameof(IsAutoStartEnabled), await _autoStartService.IsAutoStartEnabledAsync());
	}

	async partial void OnSaveRecordingToFileStorageChanged(bool value)
	{
		if (_isFirstStart)
		{
			_isFirstStart = false;
			return;
		}

		if (value)
		{
			var pickResult = await FolderPicker.PickAsync(CancellationToken.None);
			if (pickResult.IsSuccessful)
			{
				RecordingsFolder = pickResult.Folder.Path;
			}
		}
		else
		{
			RecordingsFolder = null;
		}

		_preferences.Set(nameof(RecordingsFolder), RecordingsFolder);
	}

    partial void OnIsPowerSavingModeEnabledChanged(bool value)
    {
        _preferences.Set(nameof(IsPowerSavingModeEnabled), value);
    }
}