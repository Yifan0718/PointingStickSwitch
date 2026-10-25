using System;
using System.Collections.Generic;
using System.Linq;

namespace PointingStickSwitch
{
    internal interface ILogicalStore
    {
        List<Intent> Read();
        void Write(string token, bool? enabled);
    }
    internal sealed class LogicalStore : ILogicalStore
    {
        public List<Intent> Read() { return UserSettings.ReadLogical(); }
        public void Write(string token, bool? enabled)
        { if (enabled.HasValue) UserSettings.SaveLogical(token, enabled.Value); else UserSettings.ForgetLogical(token); }
    }
    // The caller owns the cross-process mutex for the entire transaction.
    // Keeping this policy separate lets tests exercise failures without vendor hardware.
    internal sealed class LogicalController
    {
        private readonly Func<string, SynReply> call;
        private readonly ILogicalStore store;
        internal LogicalController(Func<string, SynReply> call, ILogicalStore store)
        { this.call = call; this.store = store; }
        internal Target Apply(string token, bool? requested, bool persist)
        {
            if (!Wire.ValidToken(token)) throw new ArgumentException("无效的设备标识。");
            var original = store.Read().SingleOrDefault(x => x.Token == token);
            // Re-read after acquiring the mutex: an agent's earlier snapshot may
            // predate an interactive change or a removed preference.
            if (!persist)
            {
                if (original == null) return null;
                requested = original.Enabled;
            }
            var before = call("LIST");
            if (!String.IsNullOrEmpty(before.Error)) throw new InvalidOperationException(before.Error);
            var matches = before.Targets.Where(x => x.Token == token).ToList();
            if (matches.Count != 1 || !matches[0].CanSwitch) throw new InvalidOperationException("所选逻辑指点杆不可用或身份不唯一。");
            bool enabled = requested ?? !matches[0].Enabled;
            bool attempted = false;
            try
            {
                if (persist) store.Write(token, enabled);
                attempted = true;
                var reply = call("SET|" + token + "|" + (enabled ? "1" : "0"));
                if (!String.IsNullOrEmpty(reply.Error)) throw new InvalidOperationException(reply.Error);
                var after = reply.Targets.Single(x => x.Token == token);
                if (after.Enabled != enabled || !after.CanSwitch) throw new InvalidOperationException("厂商接口状态未确认。");
                return after;
            }
            catch (Exception failure)
            {
                var errors = new List<string> { failure.Message };
                if (persist) try { store.Write(token, original == null ? (bool?)null : original.Enabled); }
                catch (Exception ex) { errors.Add("撤销保存失败：" + ex.Message); }
                // A storage rollback failure must not prevent a hardware recovery.
                if (attempted) try
                {
                    var rollback = call("SET|" + token + "|" + (matches[0].Enabled ? "1" : "0"));
                    var restored = rollback.Targets.SingleOrDefault(x => x.Token == token);
                    if (!String.IsNullOrEmpty(rollback.Error) || restored == null || !restored.CanSwitch || restored.Enabled != matches[0].Enabled)
                        throw new InvalidOperationException("厂商接口恢复读回未确认。" + rollback.Error);
                }
                catch (Exception ex) { errors.Add("恢复尚未确认：" + ex.Message); }
                throw new InvalidOperationException(String.Join("\n", errors), failure);
            }
        }
    }
}
