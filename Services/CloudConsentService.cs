using System.Threading.Tasks;
using System.Windows;
using NewDesk.Dialogs;
using NewDesk.Models.Ai;

namespace NewDesk.Services;

public static class CloudConsentService
{
    public static async Task<bool> ShowInteractiveConsentAsync(Window? owner, CloudSendPreview preview)
    {
        if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
        {
            return await Application.Current.Dispatcher.InvokeAsync(async () => await ShowInteractiveConsentAsync(owner, preview)).Result;
        }

        var dlg = new CloudAiConsentDialog(preview);
        if (owner != null && owner.IsLoaded && owner.IsVisible)
        {
            dlg.Owner = owner;
        }

        bool? res = dlg.ShowDialog();
        return res == true && dlg.IsAllowed;
    }
}
