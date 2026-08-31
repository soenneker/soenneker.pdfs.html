using Microsoft.Playwright;
using System;

namespace Soenneker.Pdfs.Html.Options;

/// <summary>
/// Configures the shared Chromium browser and parallel rendering limit.
/// </summary>
public sealed class HtmlPdfUtilOptions
{
    /// <summary>
    /// Gets or sets the maximum number of documents that may render concurrently. The default is 4.
    /// </summary>
    public int MaxConcurrency { get; set; } = 4;

    /// <summary>
    /// Gets or sets the maximum time spent rendering one document after the shared browser is ready. The default is one minute.
    /// Use <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> to disable the timeout.
    /// </summary>
    public TimeSpan RenderTimeout { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets or sets the shared Chromium launch options. By default, the bundled <c>chromium</c> channel is used.
    /// </summary>
    public BrowserTypeLaunchOptions LaunchOptions { get; set; } = new() {Channel = "chromium"};
}
