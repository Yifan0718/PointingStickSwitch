using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace PointingStickSwitch
{
    internal sealed class DeviceRecord
    {
        public string Id = "", ClassGuid = "", Name = "", Driver = "", Provider = "", Section = "", Service = "";
        public string[] Hardware = new string[0], Compatible = new string[0], Ancestors = new string[0];
        public uint Status, Problem;
        public bool Present = true;
        public bool Enabled { get { return Present && (Status & 0x400) == 0 && (Status & 8) != 0; } }
        public bool Disabled { get { return Present && (Status & 0x400) != 0 && Problem == 22; } }
        public bool Healthy { get { return Enabled || Disabled; } }
        public string Token { get { return Hash("PNP|" + Id.ToUpperInvariant()); } }
        public string Identity { get { return Hash(Id.ToUpperInvariant() + "|" + ClassGuid.ToUpperInvariant() + "|" + String.Join("|", Hardware.Select(x => x.ToUpperInvariant()).OrderBy(x => x)) + "|" + Driver + "|" + Service); } }
        public static string Hash(string value)
        { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", ""); }
        public string State { get { return Enabled ? "已启用" : Disabled ? "已关闭" : "设备异常：" + Problem; } }
    }

    internal sealed class Profile
    {
        public string Family, Name, Evidence, Scope;
        public Profile(string family, string name, string evidence, string scope)
        { Family = family; Name = name; Evidence = evidence; Scope = scope; }
    }
    internal interface IDeviceBackend
    {
        List<DeviceRecord> Enumerate();
        void Set(DeviceRecord device, bool enabled);
        void Wait(int milliseconds);
    }
    internal sealed class Intent
    {
        public string Token { get; set; }
        public bool Enabled { get; set; }
    }
    internal interface IIntentStore
    {
        List<Intent> Read();
        void Write(List<Intent> intents);
    }
    internal sealed class Target
    {
        public string Token, Name, Family, Id, State, Evidence, Scope, Driver;
        public bool Enabled, Disabled, CanSwitch;
        public override string ToString() { return Name + " · " + State; }
    }
    internal static class Safety
    {
        internal const string Mouse = "4d36e96f-e325-11ce-bfc1-08002be10318";
        internal const string Keyboard = "4d36e96b-e325-11ce-bfc1-08002be10318";
        internal const string Hid = "745a17a0-74d3-11d0-b6fe-00a0c90f57da";
        internal static string GuidText(string input) { return (input ?? "").Trim('{', '}').ToLowerInvariant(); }
        internal static bool Same(string a, string b) { return String.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        internal static bool Descendant(DeviceRecord child, DeviceRecord parent) { return child.Ancestors.Any(x => Same(x, parent.Id)); }
        internal static bool Protected(DeviceRecord d)
        {
            return GuidText(d.ClassGuid) == Keyboard || d.Compatible.Concat(d.Hardware).Any(x =>
                x.IndexOf("UP:000D", StringComparison.OrdinalIgnoreCase) >= 0 ||
                x.IndexOf("SYSTEM_KEYBOARD", StringComparison.OrdinalIgnoreCase) >= 0);
        }
        internal static void CheckTopology(DeviceRecord target, List<DeviceRecord> snapshot)
        {
            if (Protected(target) || snapshot.Any(x => Descendant(x, target) && Protected(x)))
                throw new InvalidOperationException("这是键盘或触摸输入的共享接口，请选择独立指点杆接口。");
        }
        internal static bool OtherInputsChanged(List<DeviceRecord> before, List<DeviceRecord> after, DeviceRecord target)
        {
            foreach (var old in before.Where(x => x.Enabled && !Same(x.Id, target.Id) && !Descendant(x, target)))
            {
                var current = after.SingleOrDefault(x => Same(x.Id, old.Id));
                if (current == null || !current.Enabled) return true;
            }
            return false;
        }
    }
    internal sealed class Engine
    {
        private readonly IDeviceBackend backend;
        private readonly IIntentStore store;
        private readonly object gate = new object();
        internal Engine(IDeviceBackend backend, IIntentStore store) { this.backend = backend; this.store = store; }
        internal List<Target> List()
        {
            lock (gate) return backend.Enumerate().Select(d => MakeTarget(d)).Where(x => x != null).ToList();
        }
        internal static Target MakeTarget(DeviceRecord d)
        {
            Profile p = Catalog.Match(d);
            if (p == null || !d.Present) return null;
            return new Target { Token = d.Token, Id = d.Id, Name = p.Name, Family = p.Family, State = d.State,
                Enabled = d.Enabled, Disabled = d.Disabled, CanSwitch = d.Healthy, Evidence = p.Evidence, Scope = p.Scope, Driver = d.Driver };
        }
        internal static DeviceRecord Resolve(IEnumerable<DeviceRecord> records, string token)
        {
            if (!Wire.ValidToken(token)) throw new ArgumentException("无效的设备标识。");
            var matches = records.Where(d => d.Present && d.Token == token && Catalog.Match(d) != null).ToList();
            if (matches.Count != 1) throw new InvalidOperationException("所选指点杆已移除、身份不明确或不受支持。请刷新设备列表。");
            return matches[0];
        }
        internal Target Apply(string token, bool? requested, bool persist)
        {
            lock (gate)
            {
                var before = backend.Enumerate();
                var target = Resolve(before, token);
                if (!target.Healthy) throw new InvalidOperationException(target.State + "；请检查设备后重试。");
                bool enabled = requested ?? !target.Enabled;
                if (!enabled) Safety.CheckTopology(target, before);
                // Save the explicit intent before actuation. A process/power failure can be
                // reconciled at the next service start. Never persist volatile DEVINST values.
                List<Intent> previous = null;
                if (persist)
                {
                    previous = store.Read();
                    var next = previous.Where(x => x.Token != token).Select(x => new Intent { Token = x.Token, Enabled = x.Enabled }).ToList();
                    next.Add(new Intent { Token = token, Enabled = enabled });
                    try { store.Write(next); }
                    catch (Exception failure)
                    {
                        try { store.Write(previous); }
                        catch (Exception rollback) { throw new InvalidOperationException(failure.Message + "\n保存记录的撤销未确认：" + rollback.Message, failure); }
                        throw;
                    }
                }
                bool attempted = false;
                try
                {
                    if (target.Enabled != enabled)
                    {
                        var fresh = backend.Enumerate();
                        var live = Resolve(fresh, token);
                        if (live.Identity != target.Identity) throw new InvalidOperationException("驱动或设备身份在操作前发生变化，请刷新后重试。");
                        if (!enabled) Safety.CheckTopology(live, fresh);
                        attempted = true;
                        backend.Set(live, enabled);
                    }
                    DeviceRecord after = Confirm(token, enabled, target.Identity);
                    if (!enabled && Safety.OtherInputsChanged(before, backend.Enumerate(), target))
                        throw new InvalidOperationException("其他输入设备状态发生变化，正在恢复原状态。");
                    return MakeTarget(after);
                }
                catch (Exception failure)
                {
                    var errors = new List<string> { failure.Message };
                    // Restore the saved intent first, so an automatic reconciler will not
                    // repeat a failed request if hardware rollback itself is unavailable.
                    if (persist) try { store.Write(previous); } catch (Exception ex) { errors.Add("撤销保存失败：" + ex.Message); }
                    if (attempted) try
                    {
                        var live = Resolve(backend.Enumerate(), token);
                        if (live.Identity != target.Identity) throw new InvalidOperationException("设备身份已变化，请刷新设备");
                        backend.Set(live, target.Enabled); Confirm(token, target.Enabled, target.Identity);
                    }
                    catch (Exception ex) { errors.Add("恢复尚未确认：" + ex.Message); }
                    throw new InvalidOperationException(String.Join("\n", errors), failure);
                }
            }
        }
        private DeviceRecord Confirm(string token, bool enabled, string identity)
        {
            for (int i = 0; i < 25; i++)
            {
                var current = Resolve(backend.Enumerate(), token);
                if (current.Identity != identity) throw new InvalidOperationException("确认状态时设备或驱动身份已变化。");
                if (enabled ? current.Enabled : current.Disabled) return current;
                backend.Wait(100);
            }
            throw new TimeoutException("驱动未确认请求的状态。请刷新；可能需要厂商设置或手动重启。");
        }
        internal List<string> Restore(Func<bool> cancelled = null)
        {
            lock (gate)
            {
                var errors = new List<string>();
                foreach (var intent in store.Read())
                {
                    if (cancelled != null && cancelled()) break;
                    try { Apply(intent.Token, intent.Enabled, false); }
                    catch (Exception ex) { errors.Add(intent.Token.Substring(0, 8) + ": " + ex.Message); }
                }
                return errors;
            }
        }
        internal void ReleaseAll()
        {
            lock (gate)
            {
                var present = List();
                foreach (var saved in store.Read())
                {
                    if (present.Any(x => x.Token == saved.Token)) Apply(saved.Token, true, false);
                    else if (!saved.Enabled) throw new InvalidOperationException("有已关闭的设备未连接。请连接后恢复启用，再卸载服务。");
                }
                store.Write(new List<Intent>());
            }
        }
    }
    internal static class Selection
    {
        internal static Target Resolve(List<Target> choices, string saved)
        {
            if (!String.IsNullOrEmpty(saved))
            {
                var selected = choices.SingleOrDefault(x => x.Token == saved);
                if (selected == null) throw new InvalidOperationException("请连接已选择的指点杆，或在控制面板重新选择设备。");
                return selected;
            }
            if (choices.Count != 1) throw new InvalidOperationException(choices.Count == 0 ? "尚未找到可独立控制的指点杆。可导出诊断信息。" : "发现多个指点杆，请先在控制面板选择一个。");
            return choices[0];
        }
    }
    internal sealed class Request
    {
        internal string Command, Token;
        internal bool? Enabled;
    }
    internal static class Wire
    {
        internal static bool ValidToken(string value) { return value != null && value.Length == 64 && value.All(c => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F')); }
        internal static Request Parse(string line)
        {
            if (line == "2|LIST") return new Request { Command = "LIST" };
            var parts = (line ?? "").Split('|');
            if (parts.Length == 4 && parts[0] == "2" && parts[1] == "SET" && ValidToken(parts[2]) && (parts[3] == "0" || parts[3] == "1"))
                return new Request { Command = "SET", Token = parts[2], Enabled = parts[3] == "1" };
            if (parts.Length == 3 && parts[0] == "2" && parts[1] == "TOGGLE" && ValidToken(parts[2]))
                return new Request { Command = "TOGGLE", Token = parts[2], Enabled = null };
            throw new ArgumentException("无效的服务协议或命令。");
        }
    }
}
