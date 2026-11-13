using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HomeManagementClient.Services;

public sealed partial class UserNotifier : ObservableObject
{
    private const int MaxToastLength = 80;

#if GTK
    public static bool UsesInlineResults => true;
#else
    public static bool UsesInlineResults => false;
#endif

    [ObservableProperty]
    public partial string LastMessage { get; private set; } = string.Empty;

    public async Task ShowAsync(string message, bool isError = false, string? title = null)
    {
        if (UsesInlineResults)
        {
            ShowInline($"{title ?? (isError ? "Error" : "Done")}: {message}");
            return;
        }

        if (!isError && title is null && message.Length <= MaxToastLength && !message.Contains('\n'))
        {
            try
            {
                await Toast.Make(message, ToastDuration.Short).Show();
                return;
            }
            catch (Exception)
            {
            }
        }

        await Shell.Current.DisplayAlertAsync(title ?? (isError ? "Error" : "Done"), message, "OK");
    }

    public void ShowProgress(string message)
    {
        if (UsesInlineResults)
        {
            ShowInline(message);
        }
    }

    public Task ShowErrorAsync(Exception exception) => ShowAsync(ApiErrors.ToMessage(exception), isError: true);

    public Task<bool> ConfirmAsync(string title, string message, string accept)
        => Shell.Current.DisplayAlertAsync(title, message, accept, "Cancel");

    private void ShowInline(string message)
    {
        LastMessage = string.Join(" · ", message.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        Console.WriteLine(LastMessage);
    }
}
