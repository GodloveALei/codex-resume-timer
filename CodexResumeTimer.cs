using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--check")
        {
            try
            {
                using (var desktop = new DesktopConnection())
                    desktop.CheckAsync(ChatTarget.Parse(args[1])).GetAwaiter().GetResult();
                Console.WriteLine("Codex confirmed the target chat. No message sent.");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error.Message);
                return 1;
            }
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new ResumeTimerForm());
        return 0;
    }
}

internal sealed class ChatTarget
{
    public readonly string Id;
    public readonly string Host;

    private ChatTarget(Guid id, string host)
    {
        Id = id.ToString("D");
        Host = host;
    }

    public static ChatTarget Parse(string value)
    {
        value = value.Trim();
        Guid id;
        if (Guid.TryParseExact(value, "D", out id) && id != Guid.Empty)
            return new ChatTarget(id, "local");

        Uri uri;
        if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || uri.Scheme != "codex" ||
            uri.Host != "threads" || uri.UserInfo.Length != 0 || !uri.IsDefaultPort ||
            !Guid.TryParseExact(uri.AbsolutePath.TrimStart('/'), "D", out id) || id == Guid.Empty)
            throw new ArgumentException("请粘贴 Codex 的「复制深层链接」，或输入完整的对话 ID。");

        var host = System.Web.HttpUtility.ParseQueryString(uri.Query)["hostId"];
        if (String.IsNullOrEmpty(host))
            host = "local";
        if (!Regex.IsMatch(host, @"\A[A-Za-z0-9][A-Za-z0-9_.:-]{0,127}\z"))
            throw new ArgumentException("链接中的主机 ID 无法识别，请重新复制对话链接。");
        return new ChatTarget(id, host);
    }

    public string Link
    {
        get { return "codex://threads/" + Id + (Host == "local" ? "" : "?hostId=" + Uri.EscapeDataString(Host)); }
    }
}

internal sealed class DesktopConnection : IDisposable
{
    public const string Message = "继续";
    private const string OwnerMethod = "thread-owner-discovery";
    private const string SettingsMethod = "thread-follower-read-model-settings";
    private const string SendMethod = "thread-follower-start-turn";
    private const int MaxFrameSize = 16 * 1024 * 1024;

    private readonly NamedPipeClientStream pipe;
    private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = MaxFrameSize };
    private string clientId = "initializing-client";

    public DesktopConnection(string pipeName = "codex-ipc")
    {
        pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
    }

    public async Task CheckAsync(ChatTarget target)
    {
        var owner = await ConnectToOwnerAsync(target).ConfigureAwait(false);
        var response = await RequestAsync(SettingsMethod, 1, new { conversationId = target.Id },
            owner, RemoteHost(target), 5000).ConfigureAwait(false);
        var settings = Child(Child(response, "result"), "settings");
        if (!Success(response, SettingsMethod, owner) || settings == null)
            throw new InvalidOperationException("桌面端未能验证该对话。请打开目标对话后重试。");
    }

    public async Task<DeliveryReceipt> SendAsync(ChatTarget target)
    {
        string owner = await ConnectToOwnerAsync(target).ConfigureAwait(false);
        string messageId = Guid.NewGuid().ToString("D");
        string requestId = Guid.NewGuid().ToString("D");
        string detail = "";

        using (var deadline = new CancellationTokenSource(62000))
        using (deadline.Token.Register(() => pipe.Dispose()))
        {
            // Follow the actual user message so an error returned after delivery cannot mark it failed.
            await WritePacketAsync(new
            {
                type = "broadcast", method = "thread-stream-following-changed", version = 1,
                sourceClientId = clientId, targetClientIds = new[] { owner },
                @params = new { conversationId = target.Id, hostId = target.Host, following = true }
            }).ConfigureAwait(false);

            try
            {
                await WritePacketAsync(CreateRequest(SendMethod, 2, new
                {
                    conversationId = target.Id,
                    turnStart = new
                    {
                        request = new
                        {
                            threadId = target.Id, clientUserMessageId = messageId,
                            input = new[] { new { type = "text", text = Message, text_elements = new object[0] } }
                        },
                        context = new { inheritThreadSettings = true }
                    }
                }, owner, RemoteHost(target), requestId, 60000)).ConfigureAwait(false);

                bool responseSeen = false;
                while (true)
                {
                    var packet = await ReadPacketAsync().ConfigureAwait(false);
                    if (IsDeliveryEvent(packet, target, owner, messageId))
                        return new DeliveryReceipt(DeliveryState.Delivered, "已在原对话确认收到「继续」。");
                    if (responseSeen || Value(packet, "type") != "response" || Value(packet, "requestId") != requestId)
                        continue;
                    if (Success(packet, SendMethod, owner))
                        return new DeliveryReceipt(DeliveryState.Submitted, "桌面端已确认提交。运行结果请看原对话。");

                    detail = Value(packet, "error") ?? "";
                    responseSeen = true;
                    deadline.CancelAfter(6500);
                }
            }
            catch (Exception)
            {
                if (detail == "no-client-found")
                    return new DeliveryReceipt(DeliveryState.Failed, "目标对话已离线。请打开该对话后重新定时。");
                return new DeliveryReceipt(DeliveryState.Unconfirmed,
                    "请在原对话核对「继续」。" + (detail.Length == 0 ? "" : " 回执：" + Brief(detail)));
            }
        }
    }

    internal static bool IsDeliveryEvent(Dictionary<string, object> packet, ChatTarget target,
        string owner, string messageId)
    {
        if (Value(packet, "type") != "broadcast" || Value(packet, "sourceClientId") != owner ||
            Value(packet, "method") != "thread-stream-state-changed")
            return false;
        var payload = Child(packet, "params");
        return Value(payload, "conversationId") == target.Id && Value(payload, "hostId") == target.Host &&
            ContainsAcceptedItem(Child(payload, "change"), messageId);
    }

    private static bool ContainsAcceptedItem(object node, string messageId)
    {
        var item = node as Dictionary<string, object>;
        if (item != null)
        {
            string type = Value(item, "type");
            if (type == "userMessage" && Value(item, "clientId") == messageId)
                return true;
            if (type == "steeringUserMessage" && Value(item, "status") == "accepted" &&
                Value(item, "serverUserMessageId") != null && Value(item, "serverClientUserMessageId") == messageId)
                return true;
            foreach (object child in item.Values)
                if (ContainsAcceptedItem(child, messageId))
                    return true;
        }
        else if (node is System.Collections.IList)
        {
            foreach (object child in (System.Collections.IList)node)
                if (ContainsAcceptedItem(child, messageId))
                    return true;
        }
        return false;
    }

    private async Task<string> ConnectToOwnerAsync(ChatTarget target)
    {
        try
        {
            await Task.Run(() => pipe.Connect(3500)).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            if (!(error is TimeoutException || error is IOException || error is UnauthorizedAccessException))
                throw;
            throw new InvalidOperationException("无法连接 Codex 桌面端。请保持桌面端开启，并打开目标对话。", error);
        }

        var init = await RequestAsync("initialize", 0, new { clientType = "codex-resume-timer" },
            null, null, 4000).ConfigureAwait(false);
        clientId = Value(Child(init, "result"), "clientId");
        if (!Success(init, "initialize", null) || String.IsNullOrEmpty(clientId))
            throw new InvalidOperationException("Codex 桌面端连接验证失败，请检查当前版本。");

        var response = await RequestAsync(OwnerMethod, 1,
            new { hostId = target.Host, conversationId = target.Id }, null, null, 5000).ConfigureAwait(false);
        var owner = Value(response, "handledByClientId");
        if (!Success(response, OwnerMethod, null) || String.IsNullOrEmpty(owner))
            throw new InvalidOperationException("找不到已打开的目标对话。请在 Codex 桌面端打开该对话后重试。");
        return owner;
    }

    private async Task<Dictionary<string, object>> RequestAsync(string method, int version,
        object parameters, string owner, string host, int timeout)
    {
        string requestId = Guid.NewGuid().ToString("D");
        using (var deadline = new CancellationTokenSource(timeout + 1500))
        using (deadline.Token.Register(() => pipe.Dispose()))
        {
            await WritePacketAsync(CreateRequest(method, version, parameters, owner, host, requestId, timeout))
                .ConfigureAwait(false);
            while (true)
            {
                var packet = await ReadPacketAsync().ConfigureAwait(false);
                if (Value(packet, "type") == "response" && Value(packet, "requestId") == requestId)
                    return packet;
            }
        }
    }

    private Dictionary<string, object> CreateRequest(string method, int version,
        object parameters, string owner, string host, string requestId, int timeout)
    {
        var request = new Dictionary<string, object>
        {
            { "type", "request" }, { "requestId", requestId }, { "method", method }, { "version", version },
            { "sourceClientId", clientId }, { "params", parameters }, { "timeoutMs", timeout }
        };
        if (owner != null)
            request.Add("targetClientId", owner);
        if (host != null)
            request.Add("hostId", host);
        return request;
    }

    private Task WritePacketAsync(object packet)
    {
        byte[] body = Encoding.UTF8.GetBytes(json.Serialize(packet));
        byte[] frame = new byte[4 + body.Length];
        Buffer.BlockCopy(BitConverter.GetBytes(body.Length), 0, frame, 0, 4);
        Buffer.BlockCopy(body, 0, frame, 4, body.Length);
        return pipe.WriteAsync(frame, 0, frame.Length);
    }

    private async Task<Dictionary<string, object>> ReadPacketAsync()
    {
        var size = new byte[4];
        await ReadAsync(size).ConfigureAwait(false);
        int length = BitConverter.ToInt32(size, 0);
        if (length < 1 || length > 256 * 1024 * 1024)
            throw new IOException("Codex 桌面端返回了无法读取的数据。");

        if (length > MaxFrameSize)
        {
            // A full conversation snapshot can exceed the limit for JSON we actually inspect.
            var buffer = new byte[65536];
            while (length > 0)
            {
                int read = await pipe.ReadAsync(buffer, 0, Math.Min(length, buffer.Length)).ConfigureAwait(false);
                if (read == 0)
                    throw new IOException("Codex 桌面端连接已断开。");
                length -= read;
            }
            return null;
        }

        var body = new byte[length];
        await ReadAsync(body).ConfigureAwait(false);
        return json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(body));
    }

    private async Task ReadAsync(byte[] bytes)
    {
        int offset = 0;
        while (offset < bytes.Length)
        {
            int read = await pipe.ReadAsync(bytes, offset, bytes.Length - offset).ConfigureAwait(false);
            if (read == 0)
                throw new IOException("Codex 桌面端连接已断开。");
            offset += read;
        }
    }

    private static string RemoteHost(ChatTarget target)
    {
        return target.Host == "local" ? null : target.Host;
    }

    private static bool Success(Dictionary<string, object> response, string method, string owner)
    {
        return Value(response, "resultType") == "success" && Value(response, "method") == method &&
            (owner == null || owner == Value(response, "handledByClientId"));
    }

    private static string Brief(string detail)
    {
        detail = (detail ?? "").Trim();
        return detail.Length > 230 ? detail.Substring(0, 230) + "…" : detail;
    }

    private static Dictionary<string, object> Child(Dictionary<string, object> data, string key)
    {
        object value;
        return data != null && data.TryGetValue(key, out value) ? value as Dictionary<string, object> : null;
    }

    private static string Value(Dictionary<string, object> data, string key)
    {
        object value;
        return data != null && data.TryGetValue(key, out value) ? value as string : null;
    }

    public void Dispose()
    {
        pipe.Dispose();
    }
}

internal enum DeliveryState { Delivered, Submitted, Failed, Unconfirmed }

internal sealed class DeliveryReceipt
{
    public readonly DeliveryState State;
    public readonly string Detail;

    public DeliveryReceipt(DeliveryState state, string detail)
    {
        State = state;
        Detail = detail;
    }
}
