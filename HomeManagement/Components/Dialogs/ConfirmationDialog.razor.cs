using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace HomeManagement.Components.Dialogs;

public partial class ConfirmationDialog
{
    [CascadingParameter] private IMudDialogInstance DialogReference { get; set; } = default!;

    [Parameter] public string Message { get; set; } = string.Empty;
    [Parameter] public string ConfirmText { get; set; } = Strings.Common_Delete;
    [Parameter] public string CancelText { get; set; } = Strings.Common_Cancel;

    private void Confirm() => DialogReference.Close(DialogResult.Ok(true));

    private void Cancel() => DialogReference.Cancel();
}
