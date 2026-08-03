using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Verso.Localization;

namespace Verso.Blazor.Localization;

/// <summary>
/// Interface language for the server-hosted notebook UI.
/// </summary>
/// <remarks>
/// Two entry points reach the same application: running <c>Verso.Blazor</c> directly, and
/// <c>verso serve</c>, which builds an equivalent host inside the CLI. They configure this the
/// same way from here rather than each growing their own copy.
/// </remarks>
public static class VersoLocalization
{
    /// <summary>
    /// Negotiates the interface language for each request, or pins it when one was asked for.
    /// </summary>
    /// <remarks>
    /// With no explicit language the browser decides through <c>Accept-Language</c>, which is the
    /// right answer for a server someone else might open. An explicit language removes the
    /// providers so every request gets it, which is the right answer for
    /// <c>verso serve --language de</c>.
    /// <para>
    /// Only the interface language moves. This process runs the kernels, so the formatting
    /// culture is pinned to whatever the process started with: a menu language must not decide
    /// how a cell's own results are written out.
    /// </para>
    /// Call this before the components are mapped. A Blazor circuit takes its culture from the
    /// request that opened it, so anything registered afterwards arrives too late.
    /// </remarks>
    /// <param name="app">The application being configured.</param>
    /// <param name="requestedLanguage">A language tag from the command line, or <c>null</c>.</param>
    public static WebApplication UseVersoLocalization(this WebApplication app, string? requestedLanguage)
    {
        var pinned = VersoCultures.TryMatch(requestedLanguage, out var explicitCulture);
        var uiCulture = pinned ? explicitCulture : VersoCultures.Resolve(null);
        var formattingCulture = CultureInfo.CurrentCulture;

        var options = new RequestLocalizationOptions
        {
            DefaultRequestCulture = new RequestCulture(formattingCulture, uiCulture),
            ApplyCurrentCultureToResponseHeaders = true,
        };

        // One supported formatting culture means header negotiation can never move it: anything a
        // browser asks for falls back to the default, which is the process culture.
        options.SupportedCultures = new List<CultureInfo> { formattingCulture };
        options.SupportedUICultures = VersoCultures.Supported
            .Select(tag => new CultureInfo(tag))
            .Append(new CultureInfo(VersoCultures.Pseudo))
            .ToList();

        if (pinned)
            options.RequestCultureProviders.Clear();

        app.UseRequestLocalization(options);
        return app;
    }
}
