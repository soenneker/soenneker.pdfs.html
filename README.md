[![NuGet](https://img.shields.io/nuget/v/soenneker.pdfs.html.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.pdfs.html/)
[![Build](https://img.shields.io/github/actions/workflow/status/soenneker/soenneker.pdfs.html/publish-package.yml?style=for-the-badge)](https://github.com/soenneker/soenneker.pdfs.html/actions/workflows/publish-package.yml)
[![Downloads](https://img.shields.io/nuget/dt/soenneker.pdfs.html.svg?style=for-the-badge)](https://www.nuget.org/packages/soenneker.pdfs.html/)

# ![Soenneker logo](https://user-images.githubusercontent.com/4441470/224455560-91ed3ee7-f510-4041-a8d2-3fc093025112.png) Soenneker.Pdfs.Html

Generate PDFs from HTML with Chromium and Playwright.

The utility keeps one Chromium process alive, gives every render an isolated browser context, and limits concurrency so a burst of PDFs does not overwhelm the host. Chromium is installed automatically on first use.

## Installation

```bash
dotnet add package Soenneker.Pdfs.Html
```

## Quick start

Register the utility once as a singleton:

```csharp
using Soenneker.Pdfs.Html.Registrars;

builder.Services.AddHtmlPdfUtilAsSingleton();
```

Inject `IHtmlPdfUtil` and generate a file:

```csharp
using Soenneker.Pdfs.Html.Abstract;

public sealed class InvoiceService
{
    private readonly IHtmlPdfUtil _htmlPdfUtil;

    public InvoiceService(IHtmlPdfUtil htmlPdfUtil)
    {
        _htmlPdfUtil = htmlPdfUtil;
    }

    public ValueTask CreatePdf(string html, string outputPath, CancellationToken cancellationToken = default)
    {
        return _htmlPdfUtil.GenerateToFile(html, outputPath, cancellationToken: cancellationToken);
    }
}
```

`GenerateToFile` writes to a temporary sibling file and atomically replaces the destination only after generation succeeds. An existing PDF is preserved if rendering or writing fails.

## Choose an output

Return a readable stream:

```csharp
await using Stream pdf = await htmlPdfUtil.Generate(html, cancellationToken: cancellationToken);
```

Write to an existing stream:

```csharp
await htmlPdfUtil.GenerateToStream(html, response.Body, cancellationToken: cancellationToken);
```

Write or replace a file atomically:

```csharp
await htmlPdfUtil.GenerateToFile(html, "invoice.pdf", cancellationToken: cancellationToken);
```

Dispose returned streams promptly; they use pooled memory.

## Configure the service

The default limit is four active renders. Additional calls wait asynchronously for a slot. Tune this based on available CPU and memory rather than request volume alone.

```csharp
builder.Services.AddHtmlPdfUtilAsSingleton(options =>
{
    options.MaxConcurrency = 8;
    options.RenderTimeout = TimeSpan.FromSeconds(45);
    options.LaunchOptions.Headless = true;
});
```

`RenderTimeout` starts once the shared browser is ready, so the initial Chromium installation is not counted against it. If Chromium exits unexpectedly, the next request starts a replacement browser.

## Configure a document

Use `HtmlPdfOptions` for settings that vary per PDF:

```csharp
using Microsoft.Playwright;
using Soenneker.Pdfs.Html.Options;

var options = new HtmlPdfOptions
{
    ContextOptions = new BrowserNewContextOptions
    {
        Locale = "en-US",
        ViewportSize = new ViewportSize
        {
            Width = 1280,
            Height = 720
        }
    },
    ContentOptions = new PageSetContentOptions
    {
        WaitUntil = WaitUntilState.NetworkIdle
    },
    PdfOptions = new PagePdfOptions
    {
        Format = "A4",
        PrintBackground = true,
        Margin = new Margin
        {
            Top = "16mm",
            Right = "12mm",
            Bottom = "16mm",
            Left = "12mm"
        }
    }
};

await htmlPdfUtil.GenerateToFile(html, "invoice.pdf", options, cancellationToken);
```

The utility waits for `document.fonts.ready` by default. Set `WaitForFonts` to `false` when the document does not use web fonts or manages readiness itself.

## HTML and CSS tips

Chromium renders PDFs using print CSS. A small print stylesheet usually makes the largest difference:

```html
<style>
  @page {
    size: A4;
    margin: 16mm 12mm;
  }

  body {
    font-family: Arial, sans-serif;
    -webkit-print-color-adjust: exact;
    print-color-adjust: exact;
  }

  .page-break {
    break-before: page;
  }
</style>
```

Keep these behaviors in mind:

- Enable `PrintBackground` when backgrounds and colors matter.
- Use absolute URLs, `data:` URLs, or a `<base href="...">` element for relative assets. HTML is loaded with `SetContentAsync`, so it has no application URL by default.
- Embed critical fonts and images when output must be deterministic or the renderer may not have network access.
- Use Playwright's header and footer template properties in `PagePdfOptions` for page numbers and repeated content.

## Rendering untrusted HTML

JavaScript and external HTTP/HTTPS requests are allowed by default. Disable both when the HTML is not trusted:

```csharp
var safeOptions = new HtmlPdfOptions
{
    BlockNetworkRequests = true,
    ContextOptions = new BrowserNewContextOptions
    {
        JavaScriptEnabled = false
    }
};

await htmlPdfUtil.GenerateToFile(untrustedHtml, "document.pdf", safeOptions, cancellationToken);
```

Network blocking prevents remote images, stylesheets, scripts, and fonts from loading. Embedded content and `data:` resources remain available. Font readiness waiting is skipped automatically when JavaScript is disabled.

## Deployment notes

- The first render is slower because it verifies or installs Chromium. If cold-start latency matters, generate a small warm-up document when the application starts.
- The process needs write access to the Playwright browser directory used by `Soenneker.Playwrights.Installation`.
- Linux hosts may require Chromium system dependencies. The installation utility installs dependencies by default; ensure the runtime user has the necessary permissions or include them in the container image.
- Some containers running as root require Chromium's `--no-sandbox` argument. Use it only when the environment requires it, because disabling the browser sandbox reduces isolation.

```csharp
builder.Services.AddHtmlPdfUtilAsSingleton(options =>
{
    options.LaunchOptions.Args = ["--no-sandbox"];
});
```
