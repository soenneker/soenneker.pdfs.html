using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Soenneker.Pdfs.Html.Abstract;
using Soenneker.Pdfs.Html.Options;
using Soenneker.Playwrights.Installation.Registrars;
using Soenneker.Utils.File.Registrars;
using Soenneker.Utils.Path.Registrars;

namespace Soenneker.Pdfs.Html.Registrars;

/// <summary>
/// A utility library for generating PDF documents from HTML using Playwright.
/// </summary>
public static class HtmlPdfUtilRegistrar
{
    /// <summary>
    /// Adds <see cref="IHtmlPdfUtil"/> as a singleton service. <para/>
    /// </summary>
    public static IServiceCollection AddHtmlPdfUtilAsSingleton(this IServiceCollection services, Action<HtmlPdfUtilOptions>? configure = null)
    {
        services.AddOptions<HtmlPdfUtilOptions>();

        if (configure != null)
            services.Configure(configure);

        services.AddFileUtilAsSingleton()
                .AddPathUtilAsSingleton()
                .AddPlaywrightInstallationUtilAsSingleton()
                .TryAddSingleton<IHtmlPdfUtil, HtmlPdfUtil>();

        return services;
    }

}
