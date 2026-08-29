using System.Text;
using System.Text.Json;
using CCU.Shared.Models;
using Microsoft.Extensions.Logging;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;

namespace CCU.Service.Infrastructure;

/// <summary>
/// 原厂 GCUBridge MQTT 控制通道（MQTT 优先架构的核心）。
///
/// 协议来源：Mode Tray 对本机智控中心 5.60.60.17 GCUBridge 服务的实测验证。
/// - Broker: 本机 127.0.0.1:13688（由原厂 GCUService 提供）
/// - 控制 topic: Fan/Control（模式/OC/强冷共用，靠 Action 字段区分）
/// - 状态确认: 切换后轮询智控中心配置文件直到状态匹配（与 Mode Tray 一致）
///
/// 与 Mode Tray 的差异：这里是常驻连接 + 自动重连 + 串行化发布。
/// GCUBridge 仅创建 PluginClient_0..19；_19 保留给 Mode Tray，CCU 从 _18 向下选择可用槽位。
/// </summary>
public sealed class VendorMqttControl : IDisposable
{
    private sealed record Credential(string ClientId, string Username, string Password);

    private static readonly System.Reflection.MethodInfo MqttCloseMethod =
        typeof(MqttClient).GetMethod("Close",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(MqttClient).FullName, "Close");

    private readonly ILogger<VendorMqttControl> _logger;
    private readonly VendorStateReader _stateReader;
    private readonly object _sync = new(); // M2Mqtt 非线程安全：连接与发布全部串行化

    private readonly string _host;
    private readonly int _port;
    private readonly IReadOnlyList<Credential> _credentials;

    private MqttClient? _client;
    private string? _activeClientId;
    private bool _connected;

    public VendorMqttControl(
        ILogger<VendorMqttControl> logger,
        VendorStateReader stateReader,
        string? host = null,
        int? port = null,
        string? clientId = null,
        string? username = null,
        string? password = null)
    {
        _logger = logger;
        _stateReader = stateReader;
        _host = host ?? "127.0.0.1";
        _port = port ?? 13688;

        if (clientId is not null || username is not null || password is not null)
        {
            if (clientId is null || username is null || password is null)
                throw new ArgumentException("显式 MQTT 凭据必须同时提供 clientId、username 和 password。");
            _credentials = [new Credential(clientId, username, password)];
        }
        else
        {
            // 原厂 ClientManager("PluginClient", 20) 只生成 0..19；19 被 Mode Tray 使用。
            _credentials = Enumerable.Range(0, 19).Reverse()
                .Select(i => new Credential(
                    $"PluginClient_{i}",
                    $"PluginClient_User_{i}",
                    $"PluginClient_Pwd888881772688_{i}"))
                .ToArray();
        }
    }

    public bool IsConnected => Volatile.Read(ref _connected);
    public string? ActiveClientId => Volatile.Read(ref _activeClientId);

    /// <summary>建立连接（已连接则跳过）。失败抛异常，由调用方决定重试策略。</summary>
    public void EnsureConnected()
    {
        lock (_sync)
        {
            if (_connected && _client?.IsConnected == true) return;

            var oldClient = _client;
            _client = null;
            _activeClientId = null;
            Volatile.Write(ref _connected, false);
            CloseClient(oldClient);

            var rejected = new List<string>();
            foreach (var credential in _credentials)
            {
                var client = new MqttClient(_host, _port, false, null, null, MqttSslProtocols.None);
                client.MqttMsgPublishReceived += (_, _) => { /* 预留：状态 topic 订阅 */ };
                client.ConnectionClosed += (_, _) =>
                {
                    // 拒绝连接的临时 client 关闭时不能覆盖后来成功连接的状态。
                    if (ReferenceEquals(_client, client) && Volatile.Read(ref _connected))
                    {
                        Volatile.Write(ref _connected, false);
                        _logger.LogWarning("GCUBridge MQTT 连接断开 (clientId={ClientId})", credential.ClientId);
                    }
                };

                byte result;
                try
                {
                    result = client.Connect(credential.ClientId, credential.Username, credential.Password);
                }
                catch
                {
                    CloseClient(client);
                    throw;
                }

                if (result == 0 && client.IsConnected)
                {
                    _client = client;
                    _activeClientId = credential.ClientId;
                    Volatile.Write(ref _connected, true);
                    _logger.LogInformation("GCUBridge MQTT 已连接 ({Host}:{Port}, clientId={ClientId})",
                        _host, _port, credential.ClientId);
                    return;
                }

                // M2Mqtt 在 CONNACK 拒绝后不会自行关闭底层 socket；必须显式 Close。
                CloseClient(client);
                if (result == 2) // IdentifierRejected：槽位不存在或已占用，继续尝试下一个
                {
                    rejected.Add(credential.ClientId);
                    continue;
                }

                throw new InvalidOperationException(
                    $"GCUBridge MQTT 连接失败，clientId={credential.ClientId}，返回码 {result}");
            }

            throw new InvalidOperationException(
                $"GCUBridge MQTT 无可用 PluginClient 槽位（已尝试: {string.Join(", ", rejected)}）");
        }
    }

    private static void CloseClient(MqttClient? client)
    {
        if (client is null) return;
        try
        {
            // M2Mqtt.Net 4.3.0.0: CONNACK 被拒时 ReceiveThread 已启动，IsConnected 仍为 false；
            // 公开 Disconnect() 无法可靠清理，此私有 Close() 才会停止线程并关闭 channel。
            MqttCloseMethod.Invoke(client, null);
        }
        catch
        {
            // DLL 将来变更时的尽力回退；正常路径不会走到这里。
            try { client.Disconnect(); } catch { }
        }
    }

    /// <summary>确保已连接；连接失败时抛出带上下文的异常。</summary>
    private void Connected()
    {
        try
        {
            EnsureConnected();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "无法连接智控中心 GCUBridge (127.0.0.1:13688)。请确认智控中心与 GCUBridge 服务正在运行。", ex);
        }
    }

    /// <summary>
    /// 切换性能模式（含狂暴静技/极速细分与自定义 Profile），并等待原厂状态确认。
    /// </summary>
    public void SwitchMode(VendorModeDefinition mode)
    {
        Connected();
        PublishControl(new { Action = mode.Action, ProfileIndex = mode.ProfileIndex });

        if (mode.OperatingMode == 2 && mode.Silent is not null && mode.Extreme is not null)
        {
            WaitForState(s => s.OperatingMode == 2, TimeSpan.FromSeconds(5));
            PublishControl(mode.Silent == 1
                ? (object)new { Action = "SET_CPU_CORE_OFFSET_SILENT", SILENT = 1 }
                : new { Action = "SET_CPU_CORE_OFFSET_EXTREME", EXTREME = 1 });
        }

        WaitForState(mode.Matches, TimeSpan.FromSeconds(5));
        _logger.LogInformation("模式切换完成: {Label}", mode.Label);
    }

    /// <summary>
    /// 狂暴模式 GPU 超频偏移（已验证值：+150 / 0）。仅狂暴模式下可用。
    /// </summary>
    public void SetTurboOc(int offsetMHz)
    {
        Connected();
        var state = _stateReader.ReadModeState();
        if (state.OperatingMode != 2)
        {
            throw new InvalidOperationException("GPU OC 只能在狂暴模式下切换。");
        }

        PublishControl(new { Action = "SET_OPERATING_MODE_DETAIL", GpuCoreClockOffsetOC = offsetMHz });
        WaitForState(
            s => s.OperatingMode == 2 && s.TurboGpuOcOffset == offsetMHz,
            TimeSpan.FromSeconds(5));
        _logger.LogInformation("GPU OC 已切换到 {Offset} MHz", offsetMHz);
    }

    /// <summary>一键强冷开关。</summary>
    public void SetFanBoost(bool enabled)
    {
        Connected();
        PublishControl(new { Action = enabled ? "FAN_BOOST_ON" : "FAN_BOOST_OFF" });
        WaitForState(s => s.FanBoostEnabled == (enabled ? 1 : 0), TimeSpan.FromSeconds(5));
        _logger.LogInformation("强冷已切换到 {State}", enabled ? "开" : "关");
    }

    /// <summary>QoS1 发布并等待 broker 确认（3 秒超时）。</summary>
    private void PublishControl(object payload) => PublishTopic("Fan/Control", payload);

    /// <summary>向任意 vendor topic 发布（灯光等）。串行化 + QoS1 + 确认等待。</summary>
    public void PublishTopic(string topic, object payload)
    {
        lock (_sync)
        {
            var client = _client ?? throw new InvalidOperationException("MQTT 未连接");
            var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload);

            ushort messageId = 0;
            using var published = new ManualResetEventSlim(false);
            void OnPublished(object sender, MqttMsgPublishedEventArgs args)
            {
                if (args.MessageId == messageId && args.IsPublished)
                {
                    published.Set();
                }
            }

            client.MqttMsgPublished += OnPublished;
            try
            {
                messageId = client.Publish(
                    topic,
                    payloadBytes,
                    MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE,
                    retain: false);

                if (!published.Wait(TimeSpan.FromSeconds(3)))
                {
                    throw new TimeoutException("GCUBridge 未确认收到控制命令。");
                }
            }
            finally
            {
                client.MqttMsgPublished -= OnPublished;
            }
        }
    }

    private void WaitForState(Func<VendorModeState, bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            var state = _stateReader.ReadModeState();
            if (predicate(state))
            {
                return;
            }
            Thread.Sleep(100);
        }
        throw new TimeoutException("命令已发送，但智控中心状态没有在超时时间内更新。");
    }

    public void Dispose()
    {
        lock (_sync)
        {
            var client = _client;
            _client = null;
            _activeClientId = null;
            Volatile.Write(ref _connected, false);
            CloseClient(client);
        }
    }
}
