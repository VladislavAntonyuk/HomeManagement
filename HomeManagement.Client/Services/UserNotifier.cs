using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;

namespace HomeManagement.Client.Services;

public sealed class UserNotifier
{
    private const int MaxToastLength = 80;

    public async Task ShowAsync(string message, bool isError = false, string? title = null)
    {
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

    public Task ShowErrorAsync(Exception exception) => ShowAsync(ApiErrors.ToMessage(exception), isError: true);

    public Task<bool> ConfirmAsync(string title, string message, string accept)
        => Shell.Current.DisplayAlertAsync(title, message, accept, "Cancel");
}
