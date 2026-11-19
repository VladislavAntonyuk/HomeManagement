using IpCameraMaui.ViewModels;
using Microsoft.Maui.Platform;

namespace IpCameraMaui.Pages;

public partial class MainPage
{
    private readonly RtmpCameraViewModel _viewModel;

    public MainPage(RtmpCameraViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        Loaded += OnPageLoaded;
    }

    private async void OnPageLoaded(object? sender, EventArgs e)
    {
#if ANDROID
        if (Handler?.MauiContext is { } mauiContext && CameraView.ToPlatform(mauiContext) is Android.Views.SurfaceView surfaceView)
        {
            await _viewModel.Init(surfaceView);
        }
#endif
        Loaded -= OnPageLoaded;
    }

    private async void StartRtmpStreamClicked(object? sender, EventArgs e)
    {
#if ANDROID
        await _viewModel.StartRtmpStream();
#endif
    }
}