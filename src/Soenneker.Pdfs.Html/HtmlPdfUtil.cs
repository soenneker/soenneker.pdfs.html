using Microsoft.Playwright;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Soenneker.Extensions.Task;
using Soenneker.Extensions.ValueTask;
using Soenneker.Pdfs.Html.Abstract;
using Soenneker.Pdfs.Html.Options;
using Soenneker.Playwrights.Installation.Abstract;
using Soenneker.Utils.File.Abstract;
using Soenneker.Utils.MemoryStream.Abstract;
using Soenneker.Utils.Path.Abstract;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Pdfs.Html;

public sealed class HtmlPdfUtil : IHtmlPdfUtil, IDisposable, IAsyncDisposable
{
    private readonly IPlaywrightInstallationUtil _playwrightInstallationUtil;
    private readonly HtmlPdfUtilOptions _options;
    private readonly ILogger<HtmlPdfUtil> _logger;
    private readonly IFileUtil _fileUtil;
    private readonly IMemoryStreamUtil _memoryStreamUtil;
    private readonly IPathUtil _pathUtil;
    private readonly SemaphoreSlim _concurrencyGate;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private int _disposed;

    public HtmlPdfUtil(IPlaywrightInstallationUtil playwrightInstallationUtil, IOptions<HtmlPdfUtilOptions> options, ILogger<HtmlPdfUtil> logger,
        IFileUtil fileUtil, IMemoryStreamUtil memoryStreamUtil, IPathUtil pathUtil)
    {
        _playwrightInstallationUtil = playwrightInstallationUtil;
        _options = options.Value;
        _logger = logger;
        _fileUtil = fileUtil;
        _memoryStreamUtil = memoryStreamUtil;
        _pathUtil = pathUtil;

        if (_options.MaxConcurrency < 1)
            throw new ArgumentOutOfRangeException(nameof(options), _options.MaxConcurrency, "Maximum concurrency must be at least one.");

        if (_options.RenderTimeout != Timeout.InfiniteTimeSpan && _options.RenderTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), _options.RenderTimeout, "Render timeout must be positive or infinite.");

        ArgumentNullException.ThrowIfNull(_options.LaunchOptions);

        _concurrencyGate = new SemaphoreSlim(_options.MaxConcurrency, _options.MaxConcurrency);
    }

    public async ValueTask<Stream> Generate(string html, HtmlPdfOptions? options = null, CancellationToken cancellationToken = default)
    {
        byte[] bytes = await GenerateBytes(html, options, cancellationToken).NoSync();
        return await _memoryStreamUtil.Get(bytes, cancellationToken).NoSync();
    }

    public async ValueTask GenerateToStream(string html, Stream destination, HtmlPdfOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (!destination.CanWrite)
            throw new ArgumentException("The destination stream must be writable.", nameof(destination));

        byte[] bytes = await GenerateBytes(html, options, cancellationToken).NoSync();
        await destination.WriteAsync(bytes, cancellationToken).NoSync();
    }

    public async ValueTask GenerateToFile(string html, string outputPath, HtmlPdfOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        byte[] bytes = await GenerateBytes(html, options, cancellationToken).NoSync();
        string fullOutputPath = Path.GetFullPath(outputPath);
        string directory = Path.GetDirectoryName(fullOutputPath)!;
        string temporaryPath = await _pathUtil.GetRandomUniqueFilePath(directory, ".tmp", cancellationToken).NoSync();

        try
        {
            await _fileUtil.Write(temporaryPath, bytes, log: false, cancellationToken).NoSync();
            await _fileUtil.Move(temporaryPath, fullOutputPath, log: false, cancellationToken).NoSync();
        }
        finally
        {
            bool deleted = await _fileUtil.TryDelete(temporaryPath, log: false).NoSync();

            if (!deleted)
                _logger.LogWarning("Unable to remove temporary PDF file {TemporaryPath}", temporaryPath);
        }
    }

    private async ValueTask<byte[]> GenerateBytes(string html, HtmlPdfOptions? options, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(html);

        ThrowIfDisposed();
        await _concurrencyGate.WaitAsync(cancellationToken).NoSync();

        try
        {
            ThrowIfDisposed();

            IBrowser browser = await GetBrowser(cancellationToken).NoSync();

            using var renderTimeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            if (_options.RenderTimeout != Timeout.InfiniteTimeSpan)
                renderTimeoutSource.CancelAfter(_options.RenderTimeout);

            CancellationToken renderToken = renderTimeoutSource.Token;
            long startedAt = Environment.TickCount64;

            try
            {
                await using IBrowserContext context = await browser.NewContextAsync(options?.ContextOptions)
                                                                    .WaitAsync(renderToken)
                                                                    .NoSync();

                if (options?.BlockNetworkRequests == true)
                {
                    await context.RouteAsync("http://**/*", route => route.AbortAsync()).WaitAsync(renderToken).NoSync();
                    await context.RouteAsync("https://**/*", route => route.AbortAsync()).WaitAsync(renderToken).NoSync();
                }

                IPage page = await context.NewPageAsync()
                                          .WaitAsync(renderToken)
                                          .NoSync();

                await page.SetContentAsync(html, options?.ContentOptions)
                          .WaitAsync(renderToken)
                          .NoSync();

                if (options?.WaitForFonts != false && options?.ContextOptions?.JavaScriptEnabled != false)
                {
                    await page.EvaluateAsync<object?>("() => document.fonts.ready")
                              .WaitAsync(renderToken)
                              .NoSync();
                }

                byte[] bytes = await page.PdfAsync(options?.PdfOptions)
                                         .WaitAsync(renderToken)
                                         .NoSync();

                _logger.LogDebug("Generated a {PdfSize} byte PDF in {ElapsedMilliseconds} ms", bytes.Length, Environment.TickCount64 - startedAt);
                return bytes;
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested && renderTimeoutSource.IsCancellationRequested)
            {
                _logger.LogWarning("PDF generation exceeded the configured timeout of {RenderTimeout}", _options.RenderTimeout);
                throw new TimeoutException($"PDF generation exceeded the configured timeout of {_options.RenderTimeout}.", exception);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "PDF generation failed after {ElapsedMilliseconds} ms", Environment.TickCount64 - startedAt);
                throw;
            }
        }
        finally
        {
            _concurrencyGate.Release();
        }
    }

    private async ValueTask<IBrowser> GetBrowser(CancellationToken cancellationToken)
    {
        if (_browser is {IsConnected: true})
            return _browser;

        await _initializationLock.WaitAsync(cancellationToken).NoSync();

        try
        {
            ThrowIfDisposed();

            if (_browser is {IsConnected: true})
                return _browser;

            if (_browser != null)
            {
                _logger.LogWarning("The shared Chromium browser disconnected; starting a replacement");
                await _browser.DisposeAsync().NoSync();
            }

            _playwright?.Dispose();

            await _playwrightInstallationUtil.EnsureInstalled(cancellationToken).NoSync();
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation("Starting the shared Chromium browser with a maximum PDF concurrency of {MaxConcurrency}", _options.MaxConcurrency);

            IPlaywright playwright = await Playwright.CreateAsync().WaitAsync(cancellationToken).NoSync();

            try
            {
                IBrowser browser = await playwright.Chromium.LaunchAsync(_options.LaunchOptions)
                                                   .WaitAsync(cancellationToken)
                                                   .NoSync();

                _playwright = playwright;
                _browser = browser;
                return browser;
            }
            catch
            {
                playwright.Dispose();
                throw;
            }
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        for (var i = 0; i < _options.MaxConcurrency; i++)
        {
            await _concurrencyGate.WaitAsync().NoSync();
        }

        await _initializationLock.WaitAsync().NoSync();

        try
        {
            if (_browser != null)
                await _browser.DisposeAsync().NoSync();

            _playwright?.Dispose();
            _browser = null;
            _playwright = null;
        }
        finally
        {
            _initializationLock.Release();
            _initializationLock.Dispose();
            _concurrencyGate.Dispose();
        }
    }
}
