using System.Globalization;
using System.Net.Http.Headers;

namespace HomeManagement.Client.Services;

/// <summary>
/// Asks the hub for texts it formats (metric labels, operation messages) in the app language.
/// Reads the culture per request, so a language change in Settings applies at once.
/// </summary>
public sealed class AcceptLanguageHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var culture = CultureInfo.CurrentUICulture;
        var languages = request.Headers.AcceptLanguage;
        languages.Clear();
        if (!string.IsNullOrEmpty(culture.Name))
        {
            languages.Add(new StringWithQualityHeaderValue(culture.Name));
            if (!culture.IsNeutralCulture && !string.IsNullOrEmpty(culture.Parent.Name))
            {
                languages.Add(new StringWithQualityHeaderValue(culture.Parent.Name, 0.9));
            }
        }

        return base.SendAsync(request, cancellationToken);
    }
}
