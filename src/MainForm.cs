using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PointingStickSwitch
{
    internal sealed class MainForm : Form
    {
        private readonly Label state = new Label(), note = new Label(), detail = new Label();
        private readonly TableLayoutPanel selector = Panel(0);
        private readonly Button toggle = new Button(), enable = new Button(), refresh = new Button(), install = new Button(), diagnose = new Button();
        private readonly Panel viewport = new Panel();
        private readonly TableLayoutPanel root;
        private bool busy, updating;
        private readonly bool test;
        private Target selected;
        private static readonly Color Ink = Color.FromArgb(26, 38, 56), Green = Color.FromArgb(18, 117, 92);
        internal MainForm(bool testMode)
        {
            test = testMode;
            Text = "指点杆开关 · Pointing Stick Switch";
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            float scale = 1;
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Accessibility"))
                scale = Math.Max(1F, Math.Min(3F, Convert.ToSingle(key == null ? 100 : key.GetValue("TextScaleFactor", 100)) / 100F));
            Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 10F * scale);
            ClientSize = new Size(620, 670); StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(246, 248, 250);
            viewport.Dock = DockStyle.Fill; viewport.AutoScroll = true;
            root = Panel(0); root.Dock = DockStyle.Top;
            var header = Panel(24); header.BackColor = Ink;
            header.Controls.Add(Label("指点杆开关", new Font(Font.FontFamily, 22F * scale, FontStyle.Bold), Color.White));
            header.Controls.Add(Label("POINTING STICK SWITCH  /  " + AppVersion.Value, Font, Color.FromArgb(188, 202, 214)));
            var body = Panel(24);
            body.Controls.Add(Label("控制设备", Font, Ink));
            selector.Dock = DockStyle.Fill; selector.Margin = new Padding(0, 0, 0, 16);
            selector.AccessibleName = "选择要控制的指点杆";
            selector.SizeChanged += delegate { foreach (Control c in selector.Controls) c.MaximumSize = new Size(Math.Max(1, selector.ClientSize.Width), 0); };
            body.Controls.Add(selector);
            InitLabel(state); state.Font = new Font(Font.FontFamily, 21F * scale, FontStyle.Bold);
            InitLabel(note); InitLabel(detail); note.ForeColor = detail.ForeColor = Color.FromArgb(82, 100, 122);
            toggle.AutoSize = true; toggle.AutoSizeMode = AutoSizeMode.GrowAndShrink; toggle.Dock = DockStyle.Fill; toggle.Padding = new Padding(12, 16, 12, 16); toggle.Margin = new Padding(0, 6, 0, 14);
            toggle.Font = new Font(Font.FontFamily, 14F * scale, FontStyle.Bold); toggle.ForeColor = Color.White; toggle.FlatStyle = FlatStyle.Flat; toggle.FlatAppearance.BorderSize = 0;
            toggle.Click += async delegate { if (selected != null) await Change(!selected.Enabled); };
            enable.Text = "启用（恢复）"; enable.Click += async delegate { await Change(true); };
            refresh.Text = "刷新设备"; refresh.Click += async delegate { await RefreshAsync(); };
            install.Text = "安装 / 修复后台服务";
            install.Click += async delegate
            {
                SetBusy(true);
                try { int code = await Task.Run(() => Program.Elevate("--install --quiet")); if (code != 0) throw new InvalidOperationException(code == 1223 ? "已取消安装授权。" : "服务安装失败，请查看诊断和日志。"); }
                catch (Exception ex) { ShowError(ex); }
                finally { SetBusy(false); }
                await RefreshAsync();
            };
            diagnose.Text = "导出诊断";
            diagnose.Click += async delegate
            {
                using (var dialog = new SaveFileDialog { Filter = "JSON 文件|*.json", FileName = "pointing-stick-diagnostic.json" })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    SetBusy(true);
                    try { string data = await Task.Run(() => Program.Diagnostic()); File.WriteAllText(dialog.FileName, data, System.Text.Encoding.UTF8); }
                    catch (Exception ex) { ShowError(ex); }
                    finally { SetBusy(false); }
                }
            };
            body.Controls.Add(state); body.Controls.Add(note); body.Controls.Add(toggle);
            body.Controls.Add(new ResponsiveButtonRow(enable, refresh)); body.Controls.Add(new ResponsiveButtonRow(install, diagnose)); body.Controls.Add(detail);
            root.Controls.Add(header); root.Controls.Add(body); viewport.Controls.Add(root); Controls.Add(viewport);
            Wrap(header); Wrap(body);
            state.Text = "正在查找设备…"; toggle.Text = "正在检测"; toggle.Enabled = enable.Enabled = false;
            Shown += async delegate
            {
                await RefreshAsync();
                if (!IsDisposed)
                {
                    if (selector.Controls.Count > 0) ((DeviceChoice)selector.Controls[0]).FocusChoice();
                    viewport.AutoScrollPosition = Point.Empty; Fit();
                }
            };
            DpiChanged += delegate { BeginInvoke((Action)Fit); };
        }
        private static TableLayoutPanel Panel(int padding)
        {
            var panel = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Dock = DockStyle.Fill, Padding = new Padding(padding), Margin = Padding.Empty };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ControlAdded += delegate { while (panel.RowStyles.Count < panel.Controls.Count) panel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); };
            return panel;
        }
        private static void InitLabel(Label label) { label.AutoSize = true; label.Margin = new Padding(0, 0, 0, 12); }
        private static Label Label(string text, Font font, Color color) { var label = new Label { Text = text, Font = font, ForeColor = color }; InitLabel(label); return label; }
        private static void Wrap(TableLayoutPanel panel)
        {
            Action wrap = delegate { foreach (Control c in panel.Controls) if (c is Label) c.MaximumSize = new Size(Math.Max(1, panel.ClientSize.Width - panel.Padding.Horizontal - c.Margin.Horizontal), 0); };
            panel.SizeChanged += delegate { wrap(); }; wrap();
        }
        private void Fit()
        {
            if (Location.X < -10000) return;
            var area = Screen.FromControl(this).WorkingArea;
            Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
            Location = new Point(Math.Max(area.Left, Math.Min(Left, area.Right - Width)), Math.Max(area.Top, Math.Min(Top, area.Bottom - Height)));
        }
        internal async Task RefreshAsync()
        {
            if (busy) return; SetBusy(true);
            try
            {
                var targets = test ? new List<Target> { new Target { Token = new string('A', 64), Name = "Dynabook / ThinkPad / HP / Dell 长名称指点杆", Family = "test", Enabled = true, CanSwitch = true, State = "已启用", Evidence = "厂商接口资料与模拟测试已通过；其他设备仍需实机确认。", Scope = "仅控制已经确认的指点杆接口。遇到共享触摸板接口时停止操作。", Id = new string('X', 100), Driver = "19.5.32.154" } } : await Task.Run(() => Client.List());
                if (IsDisposed) return;
                updating = true;
                foreach (Control c in selector.Controls.Cast<Control>().ToArray()) { selector.Controls.Remove(c); c.Dispose(); }
                selector.RowStyles.Clear();
                try { selected = Selection.Resolve(targets, test ? "" : UserSettings.Selected); }
                catch (InvalidOperationException ex) { selected = null; note.Text = ex.Message; }
                foreach (var item in targets)
                {
                    var target = item;
                    var choice = new DeviceChoice(target.ToString()) { MaximumSize = new Size(Math.Max(1, selector.ClientSize.Width), 0), Checked = selected == target };
                    choice.CheckedChanged += delegate
                    {
                        if (updating || !choice.Checked) return;
                        updating = true;
                        foreach (DeviceChoice other in selector.Controls) if (other != choice) other.Checked = false;
                        updating = false; selected = target;
                        if (!test) UserSettings.Selected = target.Token;
                        ShowTarget(); SetBusy(false);
                    };
                    selector.Controls.Add(choice);
                }
                updating = false; ShowTarget();
            }
            catch (Exception ex) { state.Text = "检测未完成"; note.Text = ex.Message; selected = null; }
            finally { if (!IsDisposed) SetBusy(false); }
        }
        private void ShowTarget()
        {
            if (selected == null)
            {
                state.Text = "请选择兼容设备"; detail.Text = "支持范围由设备接口决定。共享 PS/2、未知型号或缺少独立接口的设备会保留诊断模式。\n" + Client.ProbeNote;
                toggle.Text = "暂不可切换"; toggle.BackColor = Ink; return;
            }
            state.Text = "●  " + selected.State; state.ForeColor = selected.Enabled ? Green : Ink;
            toggle.Text = selected.Enabled ? "关闭指点杆" : "启用指点杆"; toggle.BackColor = selected.Enabled ? Ink : Green;
            note.Text = selected.Family == "syncom" ? "无需 UAC · 状态保存在当前用户\n登录后由用户后台程序恢复，驱动响应可能有差异。" :
                ServiceHost.Running ? "后台服务运行中 · 日常无需 UAC\n按设备记住状态，开机及重新连接后恢复。" : "首次安装后台服务后，日常切换无需 UAC。";
            detail.Text = selected.Name + "\n" + selected.Scope + "\n" + selected.Evidence + "\n驱动：" + selected.Driver + "\n" + selected.Id;
        }
        private void SetBusy(bool value)
        {
            busy = value; selector.Enabled = refresh.Enabled = install.Enabled = diagnose.Enabled = !value;
            toggle.Enabled = enable.Enabled = !value && selected != null && selected.CanSwitch;
        }
        private async Task Change(bool enabled)
        {
            if (busy || selected == null) return;
            Target target = selected; SetBusy(true); toggle.Text = "正在处理…";
            try { await Task.Run(() => Client.Change(target, enabled)); }
            catch (Exception ex) { ShowError(ex); }
            finally { if (!IsDisposed) SetBusy(false); }
            if (!IsDisposed) await RefreshAsync();
        }
        private void ShowError(Exception ex) { if (!IsDisposed) MessageBox.Show(this, ex.Message, "指点杆开关", MessageBoxButtons.OK, MessageBoxIcon.Information); }
    }
    internal sealed class ResultForm : Form
    {
        internal ResultForm(string text)
        {
            Text = "指点杆开关"; AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.FixedToolWindow;
            Controls.Add(new Label { Text = text, AutoSize = true, MaximumSize = new Size(480, 0), Padding = new Padding(24), Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 15) });
            var timer = new Timer { Interval = 1700 }; timer.Tick += delegate { Close(); }; FormClosed += delegate { timer.Dispose(); }; timer.Start();
        }
    }
}
