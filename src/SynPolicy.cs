using System;
using System.Collections.Generic;
using System.Linq;

namespace PointingStickSwitch
{
    internal sealed class SynAccess
    {
        internal Func<int> Type, Connection, Read;
        internal Func<string> Pnp;
        internal Action<int> Write;
    }
    internal static class SynPolicy
    {
        private sealed class Snapshot
        {
            internal SynAccess Access;
            internal int Type, Connection;
            internal int? Disabled;
            internal string Pnp, Token;
        }
        internal static SynReply Run(List<SynAccess> devices, string command, Action<int> wait)
        {
            var result = new SynReply { Available = true };
            var snapshots = new List<Snapshot>();
            foreach (var access in devices)
            {
                var d = new Snapshot { Access = access, Type = access.Type() };
                if (SynIdentity.IsStick(d.Type))
                {
                    d.Connection = access.Connection(); d.Pnp = access.Pnp(); d.Token = SynIdentity.Token(d.Type, d.Connection, d.Pnp);
                    d.Disabled = access.Read();
                }
                else try { d.Disabled = access.Read(); } catch (Exception) { /* not every logical device exposes this property */ }
                snapshots.Add(d);
            }
            var sticks = snapshots.Where(x => x.Token != null).ToList();
            if (command != "LIST")
            {
                var parts = (command ?? "").Split('|');
                if (parts.Length != 3 || parts[0] != "SET" || !Wire.ValidToken(parts[1]) || (parts[2] != "0" && parts[2] != "1")) throw new ArgumentException("无效的逻辑指点杆命令。");
                var matches = sticks.Where(x => x.Token == parts[1]).ToList();
                if (matches.Count != 1) throw new InvalidOperationException("逻辑指点杆已移除或身份不唯一。");
                var target = matches[0];
                if (target.Disabled != 0 && target.Disabled != 1) throw new InvalidOperationException("驱动返回未知的禁用状态。");
                if (snapshots.Any(x => x.Type == 2 && !x.Disabled.HasValue)) throw new InvalidOperationException("无法核对共享触摸板的状态，停止写入。");
                if (target.Access.Type() != target.Type || target.Access.Connection() != target.Connection || !Safety.Same(target.Access.Pnp(), target.Pnp))
                    throw new InvalidOperationException("逻辑设备身份发生变化。");
                int wanted = parts[2] == "1" ? 0 : 1;
                if (target.Disabled != wanted)
                {
                    try
                    {
                        target.Access.Write(wanted);
                        bool confirmed = false;
                        for (int i = 0; i < 15; i++)
                        {
                            if (target.Access.Type() != target.Type || target.Access.Connection() != target.Connection || !Safety.Same(target.Access.Pnp(), target.Pnp))
                                throw new InvalidOperationException("确认状态时逻辑设备身份已变化。");
                            if (target.Access.Read() == wanted) { confirmed = true; break; }
                            wait(100);
                        }
                        if (!confirmed) throw new InvalidOperationException("Synaptics 驱动未确认状态。");
                        if (snapshots.Any(x => x != target && x.Disabled.HasValue && x.Access.Read() != x.Disabled.Value))
                            throw new InvalidOperationException("另一个输入设备状态发生变化，停止保存并恢复指点杆。");
                    }
                    catch (Exception failure)
                    {
                        try
                        {
                            if (target.Access.Type() != target.Type || target.Access.Connection() != target.Connection || !Safety.Same(target.Access.Pnp(), target.Pnp)) throw new InvalidOperationException("身份变化");
                            target.Access.Write(target.Disabled.Value);
                            if (target.Access.Read() != target.Disabled.Value) throw new InvalidOperationException("恢复读回不符");
                        }
                        catch (Exception rollback) { throw new InvalidOperationException(failure.Message + "\n恢复尚未确认：" + rollback.Message, failure); }
                        throw;
                    }
                }
                target.Disabled = wanted;
            }
            foreach (var d in sticks)
            {
                bool unique = sticks.Count(x => x.Token == d.Token) == 1;
                result.Targets.Add(new Target { Token = d.Token, Id = d.Pnp, Family = "syncom", Name = "Synaptics 逻辑指点杆",
                    Enabled = d.Disabled == 0, Disabled = d.Disabled == 1, CanSwitch = unique && (d.Disabled == 0 || d.Disabled == 1),
                    State = !unique ? "身份重复，无法独立控制" : d.Disabled == 0 ? "已启用" : d.Disabled == 1 ? "已关闭" : "驱动状态未知",
                    Evidence = "厂商 SynCOM 类型 " + d.Type + "；本机 HIDPD 接口验证，其他驱动待验证",
                    Scope = "厂商逻辑指点杆；按键是否一起关闭取决于驱动", Driver = "连接类型 " + d.Connection });
            }
            return result;
        }
    }
}
