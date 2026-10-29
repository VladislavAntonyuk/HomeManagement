using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace HomeManagement.Components.Dialogs;

public partial class DeviceEditDialog
{
    [CascadingParameter] private IMudDialogInstance DialogReference { get; set; } = default!;
    [Parameter] public DeviceEditModel Model { get; set; } = new();

    private MudForm _form = default!;

    private async Task Save()
    {
        await _form.ValidateAsync();
        if (!_form.IsValid)
        {
            return;
        }

        DialogReference.Close(DialogResult.Ok(Model));
    }

    private void Cancel() => DialogReference.Close(DialogResult.Cancel());
}
