using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace MyLocalHost.Services;

/// <summary>1件のアクセスの記録。</summary>
public sealed record RequestLog(string Method, string Path, int StatusCode, TimeSpan Elapsed);

/// <summary>
/// 1つのフォルダを http://localhost:{port}/ で配信する小さな静的ファイルサーバー（Kestrel）。
/// - テキスト系の Content-Type には charset=utf-8 を付ける（文字化け対策）
/// - Cache-Control: no-store を付ける（修正がすぐ反映されるように）
/// - フォルダへのアクセスでは index.html などを返す
/// </summary>
public sealed class StaticSiteServer : IAsyncDisposable
{
    private static readonly string[] Utf8Types =
    {
        "application/javascript",
        "application/json",
        "application/manifest+json",
        "application/xml",
        "image/svg+xml",
    };

    private WebApplication? _app;

    public StaticSiteServer(string folder, int port)
    {
        Folder = folder;
        Port = port;
    }

    public string Folder { get; }
    public int Port { get; }
    public bool IsRunning => _app is not null;

    /// <summary>
    /// 1件のリクエストに応答し終えたとき（Kestrel のスレッドから呼ばれる）。
    /// </summary>
    public event Action<RequestLog>? RequestCompleted;

    /// <summary>
    /// 起動する。同じアドレスがすでに使われていれば IOException が投げられる。
    /// 注意: 他のプロセスが 0.0.0.0 で待ち受けている場合は、127.0.0.1 での起動が成功してしまうことがある。
    /// そのため、起動前に PortChecker で確認すること。
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_app is not null) return;
        if (!Directory.Exists(Folder))
            throw new DirectoryNotFoundException($"フォルダが見つかりません: {Folder}");

        // EmptyBuilder: appsettings.json や環境変数を読まない最小構成
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
        {
            ContentRootPath = Folder,
        });
        builder.WebHost.UseKestrelCore();
        builder.WebHost.ConfigureKestrel(o => o.ListenLocalhost(Port));

        var app = builder.Build();
        var files = new PhysicalFileProvider(Folder);

        // アクセスの記録
        app.Use(async (ctx, next) =>
        {
            var started = Stopwatch.GetTimestamp();
            // ブラウザが要求したままのパス（後で index.html に書き換わる前のもの）を記録する
            var requested = ctx.Request.Path + ctx.Request.QueryString;
            try
            {
                await next(ctx);
            }
            finally
            {
                RequestCompleted?.Invoke(new RequestLog(
                    ctx.Request.Method,
                    requested,
                    ctx.Response.StatusCode,
                    Stopwatch.GetElapsedTime(started)));
            }
        });

        // すべての応答にキャッシュ無効のヘッダーを付ける
        app.Use(async (ctx, next) =>
        {
            ctx.Response.OnStarting(() =>
            {
                ctx.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
                ctx.Response.Headers.Pragma = "no-cache";
                return Task.CompletedTask;
            });
            await next(ctx);
        });

        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = files,
            ContentTypeProvider = CreateContentTypeProvider(),
            ServeUnknownFileTypes = true,
            DefaultContentType = "application/octet-stream",
            OnPrepareResponse = ctx => AddUtf8Charset(ctx.Context.Response),
        });

        // 見つからなかったときの 404
        app.Run(async ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status404NotFound;
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            await ctx.Response.WriteAsync($"404 Not Found: {ctx.Request.Path}", Encoding.UTF8);
        });

        try
        {
            await app.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await app.DisposeAsync().ConfigureAwait(false);
            files.Dispose();
            throw;
        }

        _app = app;
    }

    public async Task StopAsync()
    {
        var app = _app;
        if (app is null) return;
        _app = null;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            await app.StopAsync(cts.Token).ConfigureAwait(false);
        }
        finally
        {
            await app.DisposeAsync().ConfigureAwait(false);
        }
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private static FileExtensionContentTypeProvider CreateContentTypeProvider()
    {
        var provider = new FileExtensionContentTypeProvider();
        provider.Mappings[".js"] = "text/javascript";
        provider.Mappings[".mjs"] = "text/javascript";
        provider.Mappings[".webmanifest"] = "application/manifest+json";
        provider.Mappings[".avif"] = "image/avif";
        provider.Mappings[".map"] = "application/json";
        return provider;
    }

    private static void AddUtf8Charset(HttpResponse response)
    {
        var type = response.ContentType;
        if (string.IsNullOrEmpty(type) || type.Contains("charset", StringComparison.OrdinalIgnoreCase))
            return;

        if (type.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
            Utf8Types.Contains(type, StringComparer.OrdinalIgnoreCase))
        {
            response.ContentType = type + "; charset=utf-8";
        }
    }
}
