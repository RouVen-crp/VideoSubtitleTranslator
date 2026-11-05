using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace VideoSubtitleTranslator.Tests;

/// <summary>
/// 极简的本地 HTTP 模型替身：用 <see cref="TcpListener"/> 手写 HTTP/1.1，
/// 避免 <see cref="HttpListener"/> 在受限环境（http.sys 注册失败）下不可用。
/// </summary>
internal sealed class LoopbackHttpServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly Func<string, string> _responder;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _acceptLoop;
    private int _requestCount;

    public LoopbackHttpServer(Func<string, string> responder)
    {
        _responder = responder;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public int Port { get; }

    public string BaseUrl => $"http://127.0.0.1:{Port}/";

    public int RequestCount => Volatile.Read(ref _requestCount);

    public async Task WaitForRequestsAsync(int minimumCount, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (RequestCount < minimumCount)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Expected at least {minimumCount} model request(s), got {RequestCount}.");
            await Task.Delay(10);
        }
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            _ = Task.Run(() => HandleClientAsync(client));
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using var _ = client;
        try
        {
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 8192, leaveOpen: true);
            if (await reader.ReadLineAsync() is null) return;

            var contentLength = 0;
            while (true)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(line)) break;
                var separator = line.IndexOf(':');
                if (separator > 0 &&
                    line[..separator].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(line[(separator + 1)..].Trim(), out var length))
                {
                    contentLength = length;
                }
            }

            var body = new char[contentLength];
            var read = 0;
            while (read < contentLength)
            {
                var chunk = await reader.ReadAsync(body.AsMemory(read, contentLength - read));
                if (chunk == 0) break;
                read += chunk;
            }

            Interlocked.Increment(ref _requestCount);
            var responseBody = Encoding.UTF8.GetBytes(_responder(new string(body, 0, read)));
            var header = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {responseBody.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header);
            await stream.WriteAsync(responseBody);
            await stream.FlushAsync();
        }
        catch
        {
            // 被测代码中断连接或取消时直接忽略：替身只负责可观测的请求/响应。
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        _listener.Stop();
        try { _acceptLoop.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cancellation.Dispose();
    }
}
