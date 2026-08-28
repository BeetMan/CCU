namespace CCU.Shared.IPC;

/// <summary>
/// IPC 消息类型枚举
/// 通过 Named Pipe 在 UI 和服务之间传递的消息类型
/// </summary>
public enum IpcMessageType
{
    // === 请求 (UI → Service) ===
    GetHardwareInfo,
    SetPerformanceMode,
    SetFanTable,
    SetGpuMode,
    SetDisplaySettings,
    SetKeyboardEffect,
    SetDeviceSwitch,
    SetFanBoost,      // MQTT: FAN_BOOST_ON / FAN_BOOST_OFF
    SetTurboOc,       // MQTT: SET_OPERATING_MODE_DETAIL GpuCoreClockOffsetOC
    SaveAppProfile,
    DeleteAppProfile,
    StartOtaCheck,
    EcDiagnostic,     // EC 读写诊断 (WMI)
    KernelEcDiagnostic, // EC 内核驱动诊断 (CreateFile/DeviceIoControl)

    // === 通知 (Service → UI) ===
    HardwareInfoUpdate,
    PerformanceModeChanged,
    FanTableChanged,
    GpuModeChanged,
    KeyboardEffectChanged,
    DeviceSwitchChanged,
    OsdNotification,
    OtaUpdateAvailable,

    // === 通用 ===
    Ack,
    Error
}

/// <summary>
/// 通过 Named Pipe 序列化的 IPC 消息
/// </summary>
public class IpcMessage
{
    public IpcMessageType Type { get; set; }
    public string Payload { get; set; } = "{}";
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public static IpcMessage Create(IpcMessageType type, object payload)
    {
        return new IpcMessage
        {
            Type = type,
            Payload = System.Text.Json.JsonSerializer.Serialize(payload),
        };
    }

    public T? DeserializePayload<T>() where T : class
    {
        return System.Text.Json.JsonSerializer.Deserialize<T>(Payload);
    }
}
