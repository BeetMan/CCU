// MqttProbe v3 — 主动发送命令测试键盘灯
// 参数: dotnet exec MqttProbe.dll <topic> <json-payload>

using System.Text;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;

Console.OutputEncoding = Encoding.UTF8;

string topic = args.Length > 0 ? args[0] : "Keyboard/Ctrl";
string payload = args.Length > 1 ? args[1] : "{}";

Console.WriteLine("╔══════════════════════════════════════╗");
Console.WriteLine("║   MqttProbe v3 — 主动命令测试      ║");
Console.WriteLine("╚══════════════════════════════════════╝");
Console.WriteLine();

var client = new MqttClient("127.0.0.1", 13688, false, null, null, MqttSslProtocols.None);

client.MqttMsgPublishReceived += (sender, e) =>
{
    var p = Encoding.UTF8.GetString(e.Message);
    Console.WriteLine($"  📩 RESPONSE [{e.Topic}] {p}");
};

try
{
    byte cr = client.Connect($"mqtt_cmd_{Environment.MachineName}");
    Console.WriteLine($"✅ 已连接 GCUBridge MQTT (result={cr})");

    // 订阅响应 (如果有)
    client.Subscribe(new[] { "#" }, new[] { MqttMsgBase.QOS_LEVEL_AT_MOST_ONCE });

    // 发送命令
    Console.WriteLine();
    Console.WriteLine($"📤 发送:");
    Console.WriteLine($"   Topic:   {topic}");
    Console.WriteLine($"   Payload: {payload}");

    client.Publish(topic, Encoding.UTF8.GetBytes(payload),
        MqttMsgBase.QOS_LEVEL_AT_MOST_ONCE, false);

    // 等待响应
    Console.WriteLine();
    Console.WriteLine("   等待响应 (3 秒)...");
    await Task.Delay(3000);

    Console.WriteLine("═══ 完成 ═══");
    client.Disconnect();
}
catch (Exception ex)
{
    Console.WriteLine($"❌ 错误: {ex.Message}");
}
