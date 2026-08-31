using System.IO;
using System.Linq;
using System.Text;
using System;
using System.Threading;
using System.Threading.Tasks;
using Soenneker.Pdfs.Html.Abstract;
using Soenneker.Pdfs.Html.Options;
using Soenneker.Tests.HostedUnit;
using Microsoft.Playwright;

namespace Soenneker.Pdfs.Html.Tests;

[ClassDataSource<Host>(Shared = SharedType.PerTestSession)]
public sealed class HtmlPdfUtilTests : HostedUnitTest
{
    private readonly IHtmlPdfUtil _util;

    public HtmlPdfUtilTests(Host host) : base(host)
    {
        _util = Resolve<IHtmlPdfUtil>(true);
    }

    [Test]
    public async Task GenerateInParallelProducesPdfs()
    {
        Task<Stream>[] tasks = Enumerable.Range(0, 8)
                                         .Select(index => _util.Generate($"<html><body><h1>PDF {index}</h1></body></html>").AsTask())
                                         .ToArray();

        Stream[] pdfs = await Task.WhenAll(tasks);

        try
        {
            foreach (Stream pdf in pdfs)
            {
                var signature = new byte[5];
                await pdf.ReadExactlyAsync(signature);

                await Assert.That(Encoding.ASCII.GetString(signature)).IsEqualTo("%PDF-");
            }
        }
        finally
        {
            foreach (Stream pdf in pdfs)
                await pdf.DisposeAsync();
        }
    }

    [Test]
    public async Task GenerateToFilePreservesExistingFileWhenRenderingFails()
    {
        string path = Path.GetTempFileName();
        const string existingContent = "existing content";
        await File.WriteAllTextAsync(path, existingContent);
        var threw = false;

        try
        {
            try
            {
                await _util.GenerateToFile("", path);
            }
            catch (ArgumentException)
            {
                threw = true;
            }

            await Assert.That(threw).IsTrue();
            await Assert.That(await File.ReadAllTextAsync(path)).IsEqualTo(existingContent);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task GenerateHonorsPreCanceledToken()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        var threw = false;

        try
        {
            await _util.Generate("<html><body>Cancelled</body></html>", cancellationToken: source.Token);
        }
        catch (OperationCanceledException)
        {
            threw = true;
        }

        await Assert.That(threw).IsTrue();
    }

    [Test]
    public async Task GenerateSupportsRestrictedRendering()
    {
        var options = new HtmlPdfOptions
        {
            BlockNetworkRequests = true,
            ContextOptions = new BrowserNewContextOptions {JavaScriptEnabled = false}
        };

        await using Stream pdf = await _util.Generate("<html><body>Restricted</body></html>", options);
        var signature = new byte[5];
        await pdf.ReadExactlyAsync(signature);

        await Assert.That(Encoding.ASCII.GetString(signature)).IsEqualTo("%PDF-");
    }
}
