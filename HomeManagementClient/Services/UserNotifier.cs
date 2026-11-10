using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;

namespace HomeManagementClient.Services;

/// <summary>
/// Shows short results as a toast, and failures and titled or longer results as an alert.
/// </summary>
public sealed class UserNotifier
{
    private const int MaxToastLength = 80;

#if GTK
    private const bool CanShowToast = false;
#else
    private const bool CanShowToast = true;
#endif

    public async Task ShowAsync(string message, bool isError = false, string? title = null)
    {
        if (CanShowToast && !isError && title is null && message.Length <= MaxToastLength && !message.Contains('\n'))
        {
            try
            {
                await Toast.Make(message, ToastDuration.Short).Show();
                return;
            }
            catch (Exception)
            {
                // Toasts are not available here, fall back to an alert
            }
        }

        await Shell.Current.DisplayAlertAsync(title ?? (isError ? "Error" : "Done"), message, "OK");
    }

    public Task ShowErrorAsync(Exception exception) => ShowAsync(ApiErrors.ToMessage(exception), isError: true);

    public Task<bool> ConfirmAsync(string title, string message, string accept)
        => Shell.Current.DisplayAlertAsync(title, message, accept, "Cancel");
}
