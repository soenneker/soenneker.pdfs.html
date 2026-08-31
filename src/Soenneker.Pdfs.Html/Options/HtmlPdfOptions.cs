using Microsoft.Playwright;

namespace Soenneker.Pdfs.Html.Options;

/// <summary>
/// Configures the isolated browser context, HTML loading, and PDF rendering behavior for one document.
/// </summary>
public sealed class HtmlPdfOptions
{
    /// <summary>
    /// Gets or sets whether HTTP and HTTPS requests made by the document are blocked. The default is <see langword="false"/>.
    /// </summary>
    public bool BlockNetworkRequests { get; set; }

    /// <summary>
    /// Gets or sets whether generation waits for <c>document.fonts.ready</c>. The default is <see langword="true"/>.
    /// </summary>
    public bool WaitForFonts { get; set; } = true;

    /// <summary>
    /// Gets or sets the options used when creating the isolated browser context.
    /// </summary>
    public BrowserNewContextOptions? ContextOptions { get; set; }

    /// <summary>
    /// Gets or sets the options used when loading HTML into the page.
    /// </summary>
    public PageSetContentOptions? ContentOptions { get; set; }

    /// <summary>
    /// Gets or sets the Playwright PDF rendering options.
    /// </summary>
    public PagePdfOptions? PdfOptions { get; set; }
}
