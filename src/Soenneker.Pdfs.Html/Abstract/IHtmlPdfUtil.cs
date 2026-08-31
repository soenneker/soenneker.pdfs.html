using Soenneker.Pdfs.Html.Options;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Pdfs.Html.Abstract;

/// <summary>
/// A utility library for generating PDF documents from HTML using Playwright.
/// </summary>
public interface IHtmlPdfUtil
{
    /// <summary>
    /// Generates a PDF from an HTML document and returns it as a readable stream positioned at the beginning.
    /// </summary>
    /// <param name="html">The HTML document to render.</param>
    /// <param name="options">Optional browser, page, content, and PDF settings.</param>
    /// <param name="cancellationToken">Token used to cancel browser installation and rendering.</param>
    /// <returns>A stream containing the generated PDF. The caller owns the returned stream.</returns>
    ValueTask<Stream> Generate(string html, HtmlPdfOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a PDF from an HTML document and writes it to a stream.
    /// </summary>
    /// <param name="html">The HTML document to render.</param>
    /// <param name="destination">The writable stream that receives the generated PDF.</param>
    /// <param name="options">Optional browser, page, content, and PDF settings.</param>
    /// <param name="cancellationToken">Token used to cancel browser installation, rendering, and writing.</param>
    ValueTask GenerateToStream(string html, Stream destination, HtmlPdfOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a PDF from an HTML document and atomically replaces the destination file after rendering and writing succeed.
    /// </summary>
    /// <param name="html">The HTML document to render.</param>
    /// <param name="outputPath">The path of the PDF file to create.</param>
    /// <param name="options">Optional browser, page, content, and PDF settings.</param>
    /// <param name="cancellationToken">Token used to cancel browser installation, rendering, and writing.</param>
    ValueTask GenerateToFile(string html, string outputPath, HtmlPdfOptions? options = null, CancellationToken cancellationToken = default);
}
