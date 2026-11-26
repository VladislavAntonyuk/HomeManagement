using HomeManagement.Application;
using HomeManagement.Components.Dialogs;
using MudBlazor;

namespace HomeManagement.Components;

public static class ComponentExtensions
{
    extension(IDialogService dialogService)
    {
        /// <param name="confirmText">The text of the confirm button, "Delete" by default.</param>
        /// <returns>True when the user confirmed.</returns>
        public async Task<bool> ConfirmAsync(string title, string message, string? confirmText = null)
        {
            var dialog = await dialogService.ShowAsync<ConfirmationDialog>(title, new DialogParameters<ConfirmationDialog>
            {
                { x => x.Message, message },
                { x => x.ConfirmText, confirmText ?? Strings.Common_Delete }
            });
            return await dialog.Result is { Canceled: false };
        }
    }

    extension(ISnackbar snackbar)
    {
        /// <summary>
        /// Shows the message of a result, green when it succeeded.
        /// </summary>
        public void Add(OperationResult result, Severity failure = Severity.Error, bool requireInteraction = false)
            => snackbar.Add(result.Message, result.IsSuccess ? Severity.Success : failure, options => options.RequireInteraction = requireInteraction);
    }
}
