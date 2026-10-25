using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace PointingStickSwitch
{
    internal static class Json
    {
        internal static string Write(object value) { return new JavaScriptSerializer { MaxJsonLength = 262144 }.Serialize(value); }
        internal static T Read<T>(string text) { return new JavaScriptSerializer { MaxJsonLength = 262144, RecursionLimit = 32 }.Deserialize<T>(text); }
    }
    internal sealed class RegistryIntentStore : IIntentStore
    {
        internal const string Key = @"SYSTEM\CurrentControlSet\Services\AccuPointSwitch\Parameters";
        public List<Intent> Read()
        {
            using (var key = Registry.LocalMachine.OpenSubKey(Key))
            {
                string value = Convert.ToString(key == null ? null : key.GetValue("IntentsV2"));
                if (String.IsNullOrEmpty(value)) return new List<Intent>();
                return Parse(value);
            }
        }
        internal static List<Intent> Parse(string value)
        {
                if (value == null || !value.TrimStart().StartsWith("[", StringComparison.Ordinal) || !value.TrimEnd().EndsWith("]", StringComparison.Ordinal))
                    throw new IOException("保存的设备状态不是有效列表，未执行自动恢复。");
                var raw = Json.Read<List<Dictionary<string, object>>>(value);
                if (raw == null || raw.Count > 128 || raw.Any(x => x == null || !x.ContainsKey("Token") || !(x["Token"] is string) || !x.ContainsKey("Enabled") || !(x["Enabled"] is bool)))
                    throw new IOException("保存的设备状态缺少字段或类型不正确，未执行自动恢复。");
                var list = raw.Select(x => new Intent { Token = (string)x["Token"], Enabled = (bool)x["Enabled"] }).ToList();
                if (list == null || list.Count > 128 || list.Any(x => x == null || !Wire.ValidToken(x.Token)) || list.Select(x => x.Token).Distinct().Count() != list.Count)
                    throw new IOException("保存的设备状态无效，未执行自动恢复。");
                return list;
        }
        public void Write(List<Intent> intents)
        {
            if (intents.Count > 128) throw new IOException("保存的设备数量过多。");
            using (var key = Registry.LocalMachine.OpenSubKey(Key, true))
            {
                if (key == null) throw new IOException("服务设置不存在。");
                // One registry value is the transaction boundary; no partially written
                // collection of per-device enable/disable values can be observed.
                key.SetValue("IntentsV2", Json.Write(intents), RegistryValueKind.String); key.Flush();
            }
        }
        internal void Migrate()
        {
            using (var key = Registry.LocalMachine.OpenSubKey(Key))
            {
                if (key == null || key.GetValue("IntentsV2") != null || key.GetValue("DesiredEnabled") == null) return;
                var old = new NativeBackend().Enumerate().Where(d => d.Hardware.Any(x => Catalog.Canonical(x) == @"HID\TOSB1008&COL05") && Catalog.Match(d) != null).ToList();
                if (old.Count == 1) Write(new List<Intent> { new Intent { Token = old[0].Token, Enabled = Convert.ToInt32(key.GetValue("DesiredEnabled")) != 0 } });
            }
        }
    }
    internal static class UserSettings
    {
        private const string Key = @"Software\PointingStickSwitch";
        internal static string Selected
        {
            get { using (var key = Registry.CurrentUser.OpenSubKey(Key)) return Convert.ToString(key == null ? null : key.GetValue("Selected")); }
            set { using (var key = Registry.CurrentUser.CreateSubKey(Key)) key.SetValue("Selected", value ?? ""); }
        }
        internal static void SaveLogical(string token, bool enabled)
        {
            if (!Wire.ValidToken(token)) throw new ArgumentException("无效的设备标识。");
            using (var key = Registry.CurrentUser.CreateSubKey(Key + @"\Logical")) { key.SetValue(token, enabled ? 1 : 0, RegistryValueKind.DWord); key.Flush(); }
        }
        internal static bool ParseLogicalValue(object value, RegistryValueKind kind)
        {
            if (kind != RegistryValueKind.DWord || !(value is int) || ((int)value != 0 && (int)value != 1))
                throw new IOException("保存的逻辑设备状态无效，未执行自动恢复。");
            return (int)value == 1;
        }
        internal static List<Intent> ReadLogical()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(Key + @"\Logical"))
                return key == null ? new List<Intent>() : key.GetValueNames().Where(Wire.ValidToken).Select(x => new Intent { Token = x, Enabled = ParseLogicalValue(key.GetValue(x), key.GetValueKind(x)) }).ToList();
        }
        internal static void ForgetLogical(string token)
        { using (var key = Registry.CurrentUser.CreateSubKey(Key + @"\Logical")) key.DeleteValue(token, false); }
    }
}
