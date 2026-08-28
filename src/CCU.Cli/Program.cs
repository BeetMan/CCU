using System.CommandLine;
using System.CommandLine.Invocation;
using System.Management;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CCU.Shared.IPC;
using LibreHardwareMonitor.Hardware;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;

// ============================================================
// CCU CLI v2 — 使用经典 System.CommandLine 语法
// ============================================================

var jsonOpts = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

// --- 根命令 ---
var root = new RootCommand("CCU CLI — 笔记本控制中心命令行接口")
{
    TreatUnmatchedTokensAsErrors = true
};

// ========================
// status 命令
// ========================
var statusCmd = new Command("status", "查看当前硬件状态和模式");
var statusJson = new Option<bool>("--json", () => false, "输出纯 JSON");
var statusMonitor = new Option<bool>("--monitor", () => false, "持续输出监控数据(每秒)");
statusCmd.AddOption(statusJson);
statusCmd.AddOption(statusMonitor);
statusCmd.SetHandler(async (json, monitor) =>
{
    if (monitor)
    {
        await StreamMonitor(1000, json);
        return;
    }
    var (ok, err) = await SendCommand(IpcMessageType.GetHardwareInfo, new { });
    if (!ok) { OutputError(json, err); return; }
    Output(json, new { success = true, message = "hardware info request sent" });
}, statusJson, statusMonitor);
root.AddCommand(statusCmd);

// ========================
// mode 命令
// ========================
var modeCmd = new Command("mode", "性能模式切换");
var modeArg = new Argument<string>("mode", "office | gaming | turbo | custom");
modeCmd.AddArgument(modeArg);
var modeJson = new Option<bool>("--json", () => false, "JSON 输出");
modeCmd.AddOption(modeJson);
modeCmd.SetHandler(async (mode, json) =>
{
    int m = ModeToInt(mode);
    if (m < 0){ OutputError(json, $"无效模式: {mode}"); return; }
    var (ok, err) = await SendCommand(IpcMessageType.SetPerformanceMode, new { Mode = m });
    Output(json, new { success = ok, mode, modeValue = m, error = err });
}, modeArg, modeJson);
root.AddCommand(modeCmd);

// ========================
// gpu 命令
// ========================
var gpuCmd = new Command("gpu", "GPU MUX 模式切换");
var gpuArg = new Argument<string>("mode", "igpu | dgpu | hybrid | hotswap");
gpuCmd.AddArgument(gpuArg);
var gpuJson = new Option<bool>("--json", () => false, "JSON 输出");
gpuCmd.AddOption(gpuJson);
gpuCmd.SetHandler(async (mode, json) =>
{
    int m = GpuToInt(mode);
    if (m < 0){ OutputError(json, $"无效 GPU 模式: {mode}"); return; }
    var (ok, err) = await SendCommand(IpcMessageType.SetGpuMode, new { Mode = m });
    Output(json, new { success = ok, gpuMode = mode, error = err });
}, gpuArg, gpuJson);
root.AddCommand(gpuCmd);

// ========================
// fan 命令
// ========================
var fanCmd = new Command("fan", "风扇控制");
var fanSub = new Argument<string>("subcommand", "auto | max | custom");
fanCmd.AddArgument(fanSub);
var fanJson = new Option<bool>("--json", () => false, "JSON 输出");
fanCmd.AddOption(fanJson);
var fanCpu = new Option<string?>("--cpu", "CPU 风扇曲线: \"t:d t:d ...\"");
fanCmd.AddOption(fanCpu);
var fanGpu = new Option<string?>("--gpu", "GPU 风扇曲线: \"t:d t:d ...\"");
fanCmd.AddOption(fanGpu);
fanCmd.SetHandler(async (sub, json, cpu, gpu) =>
{
    object? payload = sub.ToLower() switch
    {
        "auto" => new { Table = new { name = "Auto", fanControlRespective = false, cpuCurve = new object[0], gpuCurve = new object[0] } },
        "max" => new { Table = new { name = "Max", fanControlRespective = false, cpuCurve = new[] { new { upTemperature = 0, downTemperature = 0, duty = 100 } }, gpuCurve = new[] { new { upTemperature = 0, downTemperature = 0, duty = 100 } } } },
        "custom" => BuildCustomFanPayload(cpu, gpu),
        _ => null
    };
    if (payload == null) { OutputError(json, $"无效子命令: {sub}"); return; }
    if (payload is string err) { OutputError(json, err); return; }
    var (ok, error) = await SendCommand(IpcMessageType.SetFanTable, payload);
    Output(json, new { success = ok, fanMode = sub, error });
}, fanSub, fanJson, fanCpu, fanGpu);
root.AddCommand(fanCmd);

// ========================
// device 命令
// ========================
var deviceCmd = new Command("device", "设备开关");
var devArg = new Argument<string>("device", "webcam | dgpu | amdacp");
var stateArg = new Argument<string>("state", "on | off | toggle");
deviceCmd.AddArgument(devArg);
deviceCmd.AddArgument(stateArg);
var devJson = new Option<bool>("--json", () => false, "JSON 输出");
deviceCmd.AddOption(devJson);
deviceCmd.SetHandler(async (dev, state, json) =>
{
    string? dk = DevToKey(dev);
    if (dk == null) { OutputError(json, $"无效设备: {dev}"); return; }
    bool? enable = state.ToLower() switch { "on" => true, "off" => false, "toggle" => null, _ => (bool?)null };
    if (state.ToLower() is not ("on" or "off" or "toggle")) { OutputError(json, $"无效状态: {state}"); return; }
    var (ok, err) = await SendCommand(IpcMessageType.SetDeviceSwitch, new { Device = dk, Enable = enable });
    Output(json, new { success = ok, device = dk, state, error = err });
}, devArg, stateArg, devJson);
root.AddCommand(deviceCmd);

// ========================
// rgb 命令
// ========================
var rgbCmd = new Command("rgb", "键盘 RGB 灯效");
var rgbEffect = new Argument<string>("effect", "off|static|breathing|wave|rainbow|reactive|ripple|raindrop|neon|marquee|aurora|music|gaming|spark|flash|mix");
rgbCmd.AddArgument(rgbEffect);
var rgbSpeed = new Option<int>("--speed", () => 5, "速度 (1-10)");
rgbCmd.AddOption(rgbSpeed);
var rgbBright = new Option<int>("--bright", () => 3, "亮度 (0-4)");
rgbCmd.AddOption(rgbBright);
var rgbColor = new Option<string?>("--color", () => null, "颜色 R,G,B");
rgbCmd.AddOption(rgbColor);
var rgbJson = new Option<bool>("--json", () => false, "JSON 输出");
rgbCmd.AddOption(rgbJson);
rgbCmd.SetHandler(async (effect, speed, bright, color, json) =>
{
    int ev = RgbToInt(effect);
    if (ev < 0) { OutputError(json, $"无效灯效: {effect}"); return; }
    var cmd = new Dictionary<string, object> { ["effect"] = ev, ["speed"] = speed, ["brightness"] = bright };
    if (!string.IsNullOrEmpty(color))
    {
        var p = color.Split(','); if (p.Length == 3 && byte.TryParse(p[0], out var r) && byte.TryParse(p[1], out var g) && byte.TryParse(p[2], out var b)) cmd["color"] = new[] { r, g, b };
    }
    var (ok, err) = await SendCommand(IpcMessageType.SetKeyboardEffect, cmd);
    Output(json, new { success = ok, effect, error = err });
}, rgbEffect, rgbSpeed, rgbBright, rgbColor, rgbJson);
root.AddCommand(rgbCmd);

// ========================
// display 命令
// ========================
var displayCmd = new Command("display", "显示设置");
var displayProfile = new Argument<string>("profile", "vibrant|internet|video|lowblue|cinema|photo");
displayCmd.AddArgument(displayProfile);
var displayBright = new Option<int?>("--brightness", () => null, "亮度 (0-100)");
displayCmd.AddOption(displayBright);
var displayJson = new Option<bool>("--json", () => false, "JSON 输出");
displayCmd.AddOption(displayJson);
displayCmd.SetHandler(async (profile, brightness, json) =>
{
    int pv = DisplayToInt(profile);
    if (pv < 0) { OutputError(json, $"无效显示预设: {profile}"); return; }
    var (ok, err) = await SendCommand(IpcMessageType.SetDisplaySettings, new { ColorProfile = pv, Brightness = brightness });
    Output(json, new { success = ok, profile, error = err });
}, displayProfile, displayBright, displayJson);
root.AddCommand(displayCmd);

// ========================
// monitor 命令 (AI Agent 专用流式 JSON)
// ========================
var monitorCmd = new Command("monitor", "持续输出硬件监控 JSON — 每行一个 JSON 对象");
var monitorInterval = new Option<int>("--interval", () => 1000, "采样间隔(毫秒)");
monitorCmd.AddOption(monitorInterval);
monitorCmd.SetHandler(async (interval) =>
{
    await StreamMonitor(interval, raw: true);
}, monitorInterval);
root.AddCommand(monitorCmd);

// ========================
// invoke 命令 (通用 JSON-RPC)
// ========================
var invokeCmd = new Command("invoke", "通用 JSON 命令调用 (高级/AI Agent)");
var invokeType = new Argument<string>("type", "IPC 类型名");
invokeCmd.AddArgument(invokeType);
var invokePayload = new Argument<string>("payload", "JSON 负载 (或 - 表示 stdin)");
invokeCmd.AddArgument(invokePayload);
invokeCmd.SetHandler(async (type, payload) =>
{
    if (payload == "-") payload = await Console.In.ReadToEndAsync();
    if (!Enum.TryParse<IpcMessageType>(type, true, out var t)) { Console.WriteLine($"{{\"error\":\"未知类型: {type}\"}}"); return; }
    object p; try { p = JsonSerializer.Deserialize<object>(payload) ?? new { }; } catch { Console.WriteLine($"{{\"error\":\"无效 JSON\"}}"); return; }
    var (ok, err) = await SendCommand(t, p);
    Console.WriteLine(JsonSerializer.Serialize(new { success = ok, type, error = err }, jsonOpts));
}, invokeType, invokePayload);
root.AddCommand(invokeCmd);

// ========================
// hwprobe 命令 — 内置硬件诊断 (原 HwProbe 独立工具)
// ========================
var hwprobeCmd = new Command("hwprobe", "硬件诊断 — 探测 WMI ACPI、传感器、PnP 设备");
hwprobeCmd.SetHandler(async () =>
{
    await HwProbe();
});
root.AddCommand(hwprobeCmd);

// ========================
// subscribe 命令 — MQTT 消息监听 (逆向原厂协议)
// ========================
var subCmd = new Command("subscribe", "监听 GCUBridge MQTT 消息 — 用于逆向协议");
var subTopicOpt = new Option<string>("--topic", () => "#", "MQTT 主题 (默认 # 订阅全部)");
var subRawOpt = new Option<bool>("--raw", () => false, "输出原始 bytes (hex dump)");
subCmd.AddOption(subTopicOpt);
subCmd.AddOption(subRawOpt);
subCmd.SetHandler(async (topic, raw) =>
{
    await SubscribeMqtt(topic, raw);
}, subTopicOpt, subRawOpt);
root.AddCommand(subCmd);

// ========================
// 运行
// ========================
return await root.InvokeAsync(args);


// ======================================================================
// 辅助函数
// ======================================================================

static void Output(bool json, object? data)
{
    if (data == null) return;
    Console.WriteLine(JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = !json, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull }));
}

static void OutputError(bool json, string msg)
{
    if (json) Console.WriteLine(JsonSerializer.Serialize(new { error = msg }));
    else Console.Error.WriteLine($"错误: {msg}");
}

static async Task<(bool success, string? error)> SendCommand(IpcMessageType type, object payload)
{
    try
    {
        using var client = new PipeClient("CCU.Service.Pipe");
        if (!await client.ConnectAsync(3000)) return (false, "无法连接到 CCU 服务");
        var msg = IpcMessage.Create(type, payload);
        var resp = await client.SendAsync(msg);
        if (resp == null) return (false, "服务无响应");
        if (resp.Type == IpcMessageType.Error)
        {
            var err = resp.DeserializePayload<ErrorPayload>();
            return (false, err?.Message ?? "未知错误");
        }
        return (true, null);
    }
    catch (Exception ex) { return (false, ex.Message); }
}

static async Task StreamMonitor(int intervalMs, bool raw)
{
    using var client = new PipeClient("CCU.Service.Pipe");
    if (!await client.ConnectAsync()) { Console.Error.WriteLine($"{{\"error\":\"无法连接到 CCU 服务\"}}"); return; }
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; };
    var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    while (true)
    {
        try
        {
            var msg = await client.SendAsync(IpcMessage.Create(IpcMessageType.GetHardwareInfo, new { }));
            if (msg?.Type == IpcMessageType.HardwareInfoUpdate)
            {
                var payload = msg.DeserializePayload<object>();
                var str = JsonSerializer.Serialize(payload, raw ? null : opts);
                Console.WriteLine(str);
            }
            await Task.Delay(intervalMs);
        }
        catch (OperationCanceledException) { break; }
        catch (Exception ex) { Console.Error.WriteLine($"{{\"error\":\"{ex.Message}\"}}"); break; }
    }
}

static object? BuildCustomFanPayload(string? cpuStr, string? gpuStr)
{
    var cpu = ParseFan(cpuStr);
    var gpu = ParseFan(gpuStr);
    if (cpu == null && gpu == null) return "请指定 --cpu 或 --gpu 风扇曲线";
    return new { Table = new { name = "Custom", fanControlRespective = true, cpuCurve = cpu ?? new List<object>(), gpuCurve = gpu ?? new List<object>() } };
}

static List<object>? ParseFan(string? input)
{
    if (string.IsNullOrWhiteSpace(input)) return null;
    var r = new List<object>();
    foreach (var pair in input.Split(' ', StringSplitOptions.RemoveEmptyEntries))
    {
        var kv = pair.Split(':');
        if (kv.Length == 2 && int.TryParse(kv[0], out int t) && int.TryParse(kv[1], out int d))
            r.Add(new { upTemperature = t, downTemperature = Math.Max(0, t - 2), duty = d });
    }
    return r.Count > 0 ? r : null;
}

static int ModeToInt(string m) => m.ToLower() switch { "office" => 0, "gaming" => 1, "turbo" => 2, "custom" => 3, _ => -1 };
static int GpuToInt(string m) => m.ToLower() switch { "igpu" => 0, "dgpu" => 1, "hybrid" => 2, "hotswap" => 3, _ => -1 };
static string? DevToKey(string d) => d.ToLower() switch { "webcam" => "webcam", "dgpu" => "dgpu", "amdacp" => "amdacp", _ => null };
static int RgbToInt(string e) => e.ToLower() switch {
    "off" => 0, "static" => 1, "breathing" => 2, "wave" => 3, "reactive" => 4,
    "rainbow" => 5, "ripple" => 6, "raindrop" => 10, "neon" => 15, "marquee" => 9,
    "aurora" => 14, "music" => 34, "gaming" => 21, "spark" => 17, "flash" => 18, "mix" => 19, _ => -1
};
static int DisplayToInt(string p) => p.ToLower() switch {
    "vibrant" => 0, "internet" => 1, "video" => 2, "lowblue" => 3, "cinema" => 4, "photo" => 5, _ => -1
};

// ======================================================================
// HwProbe 内置诊断逻辑
// ======================================================================

static async Task HwProbe()
{
    Console.WriteLine("╔══════════════════════════════════════╗");
    Console.WriteLine("║   CCU HwProbe — 硬件诊断            ║");
    Console.WriteLine("╚══════════════════════════════════════╝");
    Console.WriteLine();

    // 1. WMI ACPI
    Console.WriteLine("─── ACPI EC 通信 ───");
    try
    {
        using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM meta_class WHERE __CLASS = 'AcpiTest_MULong'");
        var found = searcher.Get().Cast<ManagementObject>().Any();
        Console.WriteLine(found ? "  ✅ AcpiTest_MULong 可用 — EC 读写就绪" : "  ⚠️ AcpiTest_MULong 未找到");

        using var svcSearch = new ManagementObjectSearcher("SELECT State, StartMode FROM Win32_Service WHERE Name='GCUBridge'");
        foreach (ManagementObject svc in svcSearch.Get())
            Console.WriteLine($"  ℹ️ GCUBridge: State={svc["State"]}, StartMode={svc["StartMode"]}");
    }
    catch (Exception ex) { Console.WriteLine($"  ❌ {ex.Message}"); }
    Console.WriteLine();

    // 2. 硬件传感器
    Console.WriteLine("─── 硬件传感器 ───");
    try
    {
        var c = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true, IsBatteryEnabled = true };
        c.Open();
        foreach (var hw in c.Hardware)
        {
            hw.Update();
            var label = hw.HardwareType switch { HardwareType.Cpu => "CPU", HardwareType.GpuNvidia => "dGPU", HardwareType.GpuIntel => "iGPU", HardwareType.GpuAmd => "dGPU", _ => hw.HardwareType.ToString() };
            var temps = string.Join(" | ", hw.Sensors.Where(s => s.SensorType == SensorType.Temperature && s.Value > 0).Take(2).Select(s => $"{s.Name}:{s.Value:F1}°C"));
            var loads = string.Join(" | ", hw.Sensors.Where(s => s.SensorType == SensorType.Load && s.Value > 0).Take(2).Select(s => $"{s.Name}:{s.Value:F1}%"));
            if (!string.IsNullOrEmpty(temps) || !string.IsNullOrEmpty(loads))
                Console.WriteLine($"  ✅ {label}: {hw.Name} — {temps} {loads}");
        }
        c.Close();
    }
    catch (Exception ex) { Console.WriteLine($"  ❌ {ex.Message}"); }
    Console.WriteLine();

    // 3. PnP 设备
    Console.WriteLine("─── 关键设备 ───");
    try
    {
        var targets = new[] { ("*NVIDIA*GeForce*","NVIDIA dGPU"), ("*webcam*","Webcam"), ("*Camera*","摄像头"), ("*Bluetooth*Adapter*","蓝牙"), ("*ITE*","ITE MCU") };
        using var devSearch = new ManagementObjectSearcher(@"root\cimv2", "SELECT Name, Status FROM Win32_PnPEntity");
        var seen = new HashSet<string>();
        foreach (ManagementObject dev in devSearch.Get())
        {
            var name = (dev["Name"] ?? "").ToString();
            foreach (var (pat, label) in targets)
                if (name.Contains(pat, StringComparison.OrdinalIgnoreCase) && seen.Add(label))
                    Console.WriteLine($"  ✅ {label}: {name}");
        }
    }
    catch { Console.WriteLine("  ⚠️ PnP 查询需管理员权限"); }
    Console.WriteLine();

    // 4. 系统信息
    Console.WriteLine("─── 系统信息 ───");
    try
    {
        using var csSearch = new ManagementObjectSearcher("SELECT Manufacturer, Model, TotalPhysicalMemory FROM Win32_ComputerSystem");
        foreach (ManagementObject cs in csSearch.Get())
        {
            var ram = Convert.ToInt64(cs["TotalPhysicalMemory"] ?? 0L) / 1024 / 1024 / 1024;
            Console.WriteLine($"  ✅ {cs["Manufacturer"]} {cs["Model"]} — {ram} GB RAM");
        }
    }
    catch (Exception ex) { Console.WriteLine($"  ❌ {ex.Message}"); }
    Console.WriteLine("═══════════════════════════════════════");
}

// ======================================================================
// MQTT 订阅监听 — 连接 GCUBridge，输出所有消息
// ======================================================================

static async Task SubscribeMqtt(string topic, bool raw)
{
    Console.Error.WriteLine($"连接 GCUBridge MQTT (127.0.0.1:13688)...");
    var client = new MqttClient("127.0.0.1", 13688, false, null, null, MqttSslProtocols.None);

    client.MqttMsgPublishReceived += (sender, e) =>
    {
        var payload = raw
            ? BitConverter.ToString(e.Message).Replace("-", " ")
            : Encoding.UTF8.GetString(e.Message);

        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            time = timestamp,
            topic = e.Topic,
            payload,
            length = e.Message.Length,
            qos = e.QosLevel
        }));
    };

    try
    {
        byte result = client.Connect($"ccu_sub_{Environment.MachineName}");
        Console.Error.WriteLine($"✅ 已连接 GCUBridge MQTT (result={result})");
        Console.Error.WriteLine($"📡 订阅主题: {topic}");
        Console.Error.WriteLine("   按 Ctrl+C 停止监听...");
        Console.Error.WriteLine();

        string[] topics = { topic };
        byte[] qos = { 0 };
        client.Subscribe(topics, qos);

        // 永远等待
        var tcs = new TaskCompletionSource<bool>();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            tcs.TrySetResult(true);
        };
        await tcs.Task;

        client.Disconnect();
        Console.Error.WriteLine("监听已停止");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"❌ {ex.Message}");
    }
}

internal record ErrorPayload { public string Message { get; init; } = ""; }
