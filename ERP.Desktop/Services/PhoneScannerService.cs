using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using ERP.Desktop.Models;
using QRCoder;

namespace ERP.Desktop.Services;

public class PhoneScannerService
{
    private static readonly Lazy<PhoneScannerService> _instance = new(() => new PhoneScannerService());
    public static PhoneScannerService Instance => _instance.Value;

    private TcpListener? _tcpListener;
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;
    private Process? _tunnelProcess;

    public bool IsRunning { get; private set; }
    public string LocalIp { get; private set; } = "127.0.0.1";
    public int Port { get; private set; } = 5000;
    public string SecurityToken { get; private set; } = "";
    public string ServerUrl => $"http://{LocalIp}:{Port}/?token={SecurityToken}";
    public string? CloudflareHttpsUrl { get; private set; }
    public bool IsTunnelActive => !string.IsNullOrEmpty(CloudflareHttpsUrl);
    public string ActiveUrl => IsTunnelActive ? $"{CloudflareHttpsUrl}/?token={SecurityToken}" : ServerUrl;

    private readonly ConcurrentDictionary<string, (WebSocket Socket, ConnectedPhoneDevice Device)> _connectedClients = new();
    public IReadOnlyCollection<ConnectedPhoneDevice> ConnectedDevices => _connectedClients.Values.Select(v => v.Device).ToList();
    public ConnectedPhoneDevice? ActiveDevice => _connectedClients.Values.Select(v => v.Device).LastOrDefault();

    public event Action<ConnectedPhoneDevice>? DeviceConnected;
    public event Action<ConnectedPhoneDevice>? DeviceDisconnected;
    public event Func<string, Task<BarcodeScanResult>>? BarcodeReceived;
    public event Action<string>? LogMessage;
    public event Action<string?>? TunnelStatusChanged;

    private static readonly string HtmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "phone_scanner.html");
    private static readonly string JsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "html5-qrcode.min.js");

    private PhoneScannerService() { }

    public async Task<bool> StartServerAsync(int preferredPort = 5000)
    {
        if (IsRunning) return true;

        LocalIp = GetActiveLocalIpAddress();
        SecurityToken = Guid.NewGuid().ToString("N")[..8];

        int portToUse = preferredPort;
        bool bound = false;

        for (int i = 0; i < 20; i++)
        {
            try
            {
                _tcpListener = new TcpListener(IPAddress.Any, portToUse);
                _tcpListener.Start();
                bound = true;
                Port = portToUse;
                break;
            }
            catch
            {
                portToUse++;
            }
        }

        if (!bound || _tcpListener == null)
        {
            LogMessage?.Invoke("Port açılamadı. Lütfen farklı bir port deneyin veya güvenlik duvarını kontrol edin.");
            return false;
        }

        IsRunning = true;
        _cts = new CancellationTokenSource();
        _listenerTask = Task.Run(() => ListenLoopAsync(_cts.Token));

        LogMessage?.Invoke($"Sunucu başlatıldı: {ServerUrl}");
        return true;
    }

    public async Task StopServerAsync()
    {
        if (!IsRunning) return;

        StopCloudflareTunnel();

        IsRunning = false;
        _cts?.Cancel();

        try { _tcpListener?.Stop(); } catch { }

        foreach (var (id, client) in _connectedClients)
        {
            try
            {
                if (client.Socket.State == WebSocketState.Open)
                {
                    await client.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Sunucu durduruldu", CancellationToken.None);
                }
            }
            catch { }
        }
        _connectedClients.Clear();

        if (_listenerTask != null)
        {
            try { await _listenerTask; } catch { }
        }

        LogMessage?.Invoke("Sunucu durduruldu.");
    }

    public static string? GetCloudflaredBinaryPath()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] candidates = new[]
        {
            Path.Combine(baseDir, "Tools", "cloudflared.exe"),
            Path.Combine(baseDir, "cloudflared.exe"),
            Path.Combine(Environment.CurrentDirectory, "ERP.Desktop", "Tools", "cloudflared.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ERPStarter", "cloudflared.exe")
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }

        return null;
    }

    public async Task<(bool Success, string UrlOrError)> StartCloudflareTunnelAsync()
    {
        if (IsTunnelActive && !string.IsNullOrEmpty(CloudflareHttpsUrl))
        {
            return (true, CloudflareHttpsUrl);
        }

        if (!IsRunning)
        {
            await StartServerAsync();
        }

        string? binaryPath = GetCloudflaredBinaryPath();
        if (string.IsNullOrEmpty(binaryPath))
        {
            return (false, "Cloudflare tünel aracı (cloudflared.exe) bulunamadı. Lütfen Tools klasörünü kontrol edin.");
        }

        StopCloudflareTunnel();

        var tcs = new TaskCompletionSource<string>();
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        timeoutCts.Token.Register(() => tcs.TrySetException(new TimeoutException("Cloudflare HTTPS tünel bağlantısı zaman aşımına uğradı.")));

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = binaryPath,
                Arguments = $"tunnel --url http://127.0.0.1:{Port}",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            _tunnelProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };

            void ParseLine(string? line)
            {
                if (string.IsNullOrWhiteSpace(line)) return;
                var match = Regex.Match(line, @"https://[a-zA-Z0-9-]+\.trycloudflare\.com");
                if (match.Success)
                {
                    string url = match.Value.TrimEnd('/');
                    tcs.TrySetResult(url);
                }
            }

            _tunnelProcess.ErrorDataReceived += (s, e) => ParseLine(e.Data);
            _tunnelProcess.OutputDataReceived += (s, e) => ParseLine(e.Data);

            _tunnelProcess.Exited += (s, e) =>
            {
                if (!tcs.Task.IsCompleted)
                {
                    tcs.TrySetException(new InvalidOperationException("Cloudflare tüneli beklenmeyen şekilde kapandı."));
                }
            };

            _tunnelProcess.Start();
            _tunnelProcess.BeginErrorReadLine();
            _tunnelProcess.BeginOutputReadLine();

            string tunnelUrl = await tcs.Task;
            CloudflareHttpsUrl = tunnelUrl;
            TunnelStatusChanged?.Invoke(CloudflareHttpsUrl);
            LogMessage?.Invoke($"Cloudflare HTTPS Tüneli Aktif: {ActiveUrl}");
            return (true, CloudflareHttpsUrl);
        }
        catch (Exception ex)
        {
            StopCloudflareTunnel();
            return (false, ex.Message);
        }
    }

    public void StopCloudflareTunnel()
    {
        try
        {
            if (_tunnelProcess != null && !_tunnelProcess.HasExited)
            {
                _tunnelProcess.Kill(true);
            }
        }
        catch { }
        finally
        {
            _tunnelProcess = null;
            CloudflareHttpsUrl = null;
            TunnelStatusChanged?.Invoke(null);
            LogMessage?.Invoke("Cloudflare HTTPS tüneli durduruldu.");
        }
    }

    public async Task DisconnectDeviceAsync(string deviceId)
    {
        if (_connectedClients.TryRemove(deviceId, out var client))
        {
            try
            {
                if (client.Socket.State == WebSocketState.Open)
                {
                    await client.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Bağlantı kesildi", CancellationToken.None);
                }
            }
            catch { }
            DeviceDisconnected?.Invoke(client.Device);
        }
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _tcpListener != null)
        {
            try
            {
                var tcpClient = await _tcpListener.AcceptTcpClientAsync(ct);
                _ = Task.Run(() => HandleClientAsync(tcpClient, ct), ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!IsRunning) break;
                LogMessage?.Invoke($"Bağlantı hatası: {ex.Message}");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient tcpClient, CancellationToken ct)
    {
        using var client = tcpClient;
        using var stream = client.GetStream();

        try
        {
            // HTTP Request Header Okuma
            var headerBytes = await ReadHttpHeadersAsync(stream, ct);
            if (headerBytes.Length == 0) return;

            string headerText = Encoding.UTF8.GetString(headerBytes);
            using var reader = new StringReader(headerText);
            string? requestLine = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(requestLine)) return;

            var parts = requestLine.Split(' ');
            if (parts.Length < 2) return;

            string method = parts[0].ToUpperInvariant();
            string rawUrl = parts[1];

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string? line;
            while (!string.IsNullOrWhiteSpace(line = reader.ReadLine()))
            {
                int colonIdx = line.IndexOf(':');
                if (colonIdx > 0)
                {
                    headers[line[..colonIdx].Trim()] = line[(colonIdx + 1)..].Trim();
                }
            }

            // URL Ayrıştırma
            string path = rawUrl;
            string query = "";
            int qIdx = rawUrl.IndexOf('?');
            if (qIdx >= 0)
            {
                path = rawUrl[..qIdx];
                query = rawUrl[(qIdx + 1)..];
            }

            var queryParams = ParseQueryString(query);
            string clientToken = queryParams.GetValueOrDefault("token", "");

            // 1. WebSocket Upgrade Talebi
            if (headers.TryGetValue("Upgrade", out var upVal) && upVal.Equals("websocket", StringComparison.OrdinalIgnoreCase))
            {
                if (!ValidateToken(clientToken))
                {
                    await SendHttpResponseAsync(stream, 403, "Forbidden", "Geçersiz Güvenlik Tokenı", "text/plain");
                    return;
                }

                if (headers.TryGetValue("Sec-WebSocket-Key", out var secKey))
                {
                    await UpgradeToWebSocketAsync(stream, secKey, client, headers, ct);
                }
                return;
            }

            // 2. Statik HTML Scanner Sayfası
            if (method == "GET" && (path == "/" || path == "/scanner" || path == "/index.html"))
            {
                if (!File.Exists(HtmlPath))
                {
                    await SendHttpResponseAsync(stream, 404, "Not Found", "phone_scanner.html bulunamadı", "text/plain");
                    return;
                }

                byte[] htmlBytes = await File.ReadAllBytesAsync(HtmlPath, ct);
                await SendHttpResponseBytesAsync(stream, 200, "OK", htmlBytes, "text/html; charset=utf-8");
                return;
            }

            // 3. Javascript Kütüphaneleri (html5-qrcode, zxing vb.)
            if (method == "GET" && (path.EndsWith(".js", StringComparison.OrdinalIgnoreCase)))
            {
                string fileName = Path.GetFileName(path);
                string assetFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", fileName);
                if (File.Exists(assetFile))
                {
                    byte[] jsBytes = await File.ReadAllBytesAsync(assetFile, ct);
                    await SendHttpResponseBytesAsync(stream, 200, "OK", jsBytes, "application/javascript");
                    return;
                }
            }

            // 4. HTTP POST /api/scan (WebSocket Fallback)
            if (method == "POST" && path.Equals("/api/scan", StringComparison.OrdinalIgnoreCase))
            {
                int contentLength = 0;
                if (headers.TryGetValue("Content-Length", out var clStr) && int.TryParse(clStr, out var cl))
                {
                    contentLength = cl;
                }

                byte[] bodyBuffer = new byte[contentLength];
                int totalRead = 0;
                while (totalRead < contentLength)
                {
                    int r = await stream.ReadAsync(bodyBuffer.AsMemory(totalRead, contentLength - totalRead), ct);
                    if (r == 0) break;
                    totalRead += r;
                }

                string bodyJson = Encoding.UTF8.GetString(bodyBuffer);
                var dto = JsonSerializer.Deserialize<PhoneMessageDto>(bodyJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (dto == null || !ValidateToken(dto.Token))
                {
                    await SendHttpResponseAsync(stream, 403, "Forbidden", "{\"success\":false,\"message\":\"Geçersiz token\"}", "application/json");
                    return;
                }

                var scanRes = BarcodeReceived != null 
                    ? await BarcodeReceived.Invoke(dto.Barcode ?? "") 
                    : new BarcodeScanResult { Success = false, Message = "İşleyici bağlı değil" };

                string resJson = JsonSerializer.Serialize(scanRes);
                await SendHttpResponseAsync(stream, 200, "OK", resJson, "application/json; charset=utf-8");
                return;
            }

            // Bilinmeyen Yol
            await SendHttpResponseAsync(stream, 404, "Not Found", "Sayfa bulunamadı", "text/plain; charset=utf-8");
        }
        catch { }
    }

    private async Task UpgradeToWebSocketAsync(NetworkStream stream, string secKey, TcpClient client, Dictionary<string, string> headers, CancellationToken ct)
    {
        // 101 Switching Protocols el sıkışması
        string acceptKey = Convert.ToBase64String(SHA1.HashData(Encoding.UTF8.GetBytes(secKey.Trim() + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        string response =
            "HTTP/1.1 101 Switching Protocols\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            $"Sec-WebSocket-Accept: {acceptKey}\r\n\r\n";

        byte[] respBytes = Encoding.UTF8.GetBytes(response);
        await stream.WriteAsync(respBytes, ct);
        await stream.FlushAsync(ct);

        // Native WebSocket Wrapper
        var webSocket = WebSocket.CreateFromStream(stream, isServer: true, subProtocol: null, keepAliveInterval: TimeSpan.FromSeconds(30));

        string clientIp = ((IPEndPoint?)client.Client.RemoteEndPoint)?.Address.ToString() ?? "";
        if (headers.TryGetValue("CF-Connecting-IP", out var cfIp) && !string.IsNullOrWhiteSpace(cfIp))
        {
            clientIp = cfIp;
        }
        else if (headers.TryGetValue("X-Forwarded-For", out var xff) && !string.IsNullOrWhiteSpace(xff))
        {
            clientIp = xff.Split(',')[0].Trim();
        }
        string userAgent = headers.GetValueOrDefault("User-Agent", "Mobil Cihaz");
        string deviceName = DetermineDeviceName(userAgent);

        var device = new ConnectedPhoneDevice
        {
            DeviceName = deviceName,
            IpAddress = clientIp,
            ConnectedAt = DateTime.Now,
            LastActiveAt = DateTime.Now
        };

        _connectedClients[device.Id] = (webSocket, device);
        DeviceConnected?.Invoke(device);

        var buffer = new byte[8192];
        try
        {
            while (webSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    await HandleWebSocketMessageAsync(webSocket, device, message);
                }
            }
        }
        catch { }
        finally
        {
            _connectedClients.TryRemove(device.Id, out _);
            DeviceDisconnected?.Invoke(device);
            try
            {
                if (webSocket.State == WebSocketState.Open)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Kapatıldı", CancellationToken.None);
                }
            }
            catch { }
        }
    }

    private async Task HandleWebSocketMessageAsync(WebSocket socket, ConnectedPhoneDevice device, string json)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<PhoneMessageDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (dto == null) return;

            device.LastActiveAt = DateTime.Now;

            if (dto.Type == "hello" && !string.IsNullOrWhiteSpace(dto.Device))
            {
                device.DeviceName = dto.Device;
                DeviceConnected?.Invoke(device);
                return;
            }

            if (dto.Type == "scan" && !string.IsNullOrWhiteSpace(dto.Barcode))
            {
                device.ScanCount++;

                BarcodeScanResult scanResult;
                if (BarcodeReceived != null)
                {
                    scanResult = await BarcodeReceived.Invoke(dto.Barcode.Trim());
                }
                else
                {
                    scanResult = new BarcodeScanResult { Success = false, Barcode = dto.Barcode, Message = "ERP Satış modülü hazır değil" };
                }

                var respDto = new PhoneMessageDto
                {
                    Type = "scan_result",
                    Barcode = dto.Barcode,
                    Success = scanResult.Success,
                    ProductName = scanResult.ProductName,
                    Price = scanResult.Price,
                    Message = scanResult.Message
                };

                string respJson = JsonSerializer.Serialize(respDto);
                byte[] respBytes = Encoding.UTF8.GetBytes(respJson);

                if (socket.State == WebSocketState.Open)
                {
                    await socket.SendAsync(new ArraySegment<byte>(respBytes), WebSocketMessageType.Text, true, CancellationToken.None);
                }
            }
        }
        catch { }
    }

    private bool ValidateToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(SecurityToken)) return true;
        return string.Equals(SecurityToken, token, StringComparison.Ordinal);
    }

    private static string DetermineDeviceName(string userAgent)
    {
        if (userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase)) return "Apple iPhone";
        if (userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase)) return "Apple iPad";
        if (userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase)) return "Android Telefon";
        return "Mobil Cihaz";
    }

    public BitmapSource GenerateQrCodeBitmap(string text)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        using var qrCode = new PngByteQRCode(qrCodeData);
        byte[] qrCodeBytes = qrCode.GetGraphic(20);

        var image = new BitmapImage();
        using var ms = new MemoryStream(qrCodeBytes);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = ms;
        image.EndInit();
        image.Freeze();
        return image;
    }

    public void SetLocalIp(string ip)
    {
        if (!string.IsNullOrWhiteSpace(ip) && !string.Equals(LocalIp, ip, StringComparison.OrdinalIgnoreCase))
        {
            LocalIp = ip;
            LogMessage?.Invoke($"Aktif IP adresi güncellendi: {ServerUrl}");
        }
    }

    public static List<(string Ip, string Description)> GetAllAvailableLocalIps()
    {
        var result = new List<(string Ip, string Description)>();

        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .ToList();

            foreach (var iface in interfaces)
            {
                string desc = iface.Description.ToLowerInvariant();
                string name = iface.Name.ToLowerInvariant();

                bool isVirtual = desc.Contains("vmware") || desc.Contains("virtual") || desc.Contains("vbox") ||
                                 desc.Contains("hyper-v") || desc.Contains("vethernet") || desc.Contains("wsl") ||
                                 desc.Contains("bluetooth") || desc.Contains("tailscale") || desc.Contains("zerotier") ||
                                 name.Contains("vmware") || name.Contains("bluetooth");

                var ips = iface.GetIPProperties().UnicastAddresses
                    .Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork &&
                                !IPAddress.IsLoopback(u.Address) &&
                                !u.Address.ToString().StartsWith("169.254."))
                    .Select(u => u.Address.ToString())
                    .ToList();

                bool hasGateway = iface.GetIPProperties().GatewayAddresses.Any(g =>
                    g.Address != null &&
                    g.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !g.Address.Equals(IPAddress.Any) &&
                    !IPAddress.IsLoopback(g.Address) &&
                    !g.Address.ToString().StartsWith("169.254."));

                foreach (var ip in ips)
                {
                    if (hasGateway && !isVirtual)
                    {
                        // En öncelikli gerçek Wi-Fi / Ethernet ağ adaptörü
                        result.Insert(0, (ip, $"{ip} (⭐ {iface.Name} - Wi-Fi / İnternet)"));
                    }
                    else if (!isVirtual)
                    {
                        result.Add((ip, $"{ip} ({iface.Name})"));
                    }
                    else
                    {
                        // VMware vb. sanal ağlar en sona
                        result.Add((ip, $"{ip} (Sanal Ağ: {iface.Name})"));
                    }
                }
            }
        }
        catch { }

        if (result.Count == 0)
        {
            result.Add(("127.0.0.1", "127.0.0.1 (Yerel Loopback)"));
        }

        return result;
    }

    public static string GetActiveLocalIpAddress()
    {
        var list = GetAllAvailableLocalIps();
        return list.FirstOrDefault().Ip ?? "127.0.0.1";
    }

    private static async Task<byte[]> ReadHttpHeadersAsync(NetworkStream stream, CancellationToken ct)
    {
        var ms = new MemoryStream();
        byte[] buffer = new byte[1];
        int patternIndex = 0;
        byte[] endPattern = "\r\n\r\n"u8.ToArray();

        while (!ct.IsCancellationRequested)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(0, 1), ct);
            if (read == 0) break;

            ms.WriteByte(buffer[0]);

            if (buffer[0] == endPattern[patternIndex])
            {
                patternIndex++;
                if (patternIndex == endPattern.Length) break;
            }
            else
            {
                patternIndex = buffer[0] == endPattern[0] ? 1 : 0;
            }

            if (ms.Length > 32768) break; // header limit
        }

        return ms.ToArray();
    }

    private static async Task SendHttpResponseAsync(NetworkStream stream, int statusCode, string statusDesc, string body, string contentType)
    {
        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
        await SendHttpResponseBytesAsync(stream, statusCode, statusDesc, bodyBytes, contentType);
    }

    private static async Task SendHttpResponseBytesAsync(NetworkStream stream, int statusCode, string statusDesc, byte[] bodyBytes, string contentType)
    {
        string headers =
            $"HTTP/1.1 {statusCode} {statusDesc}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n" +
            "Access-Control-Allow-Origin: *\r\n" +
            "Connection: close\r\n\r\n";

        byte[] headerBytes = Encoding.UTF8.GetBytes(headers);
        await stream.WriteAsync(headerBytes);
        await stream.WriteAsync(bodyBytes);
        await stream.FlushAsync();
    }

    private static Dictionary<string, string> ParseQueryString(string query)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query)) return dict;

        var pairs = query.Split('&');
        foreach (var p in pairs)
        {
            var kv = p.Split('=');
            if (kv.Length == 2)
            {
                dict[Uri.UnescapeDataString(kv[0])] = Uri.UnescapeDataString(kv[1]);
            }
        }
        return dict;
    }
}
