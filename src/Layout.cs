using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace PointingStickSwitch
{
    internal sealed class ResponsiveButtonRow : TableLayoutPanel
    {
        private readonly Button left, right;
        private bool stacked, arranging;
        internal ResponsiveButtonRow(Button a, Button b)
        {
            left = a; right = b;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; Height = 1; Dock = DockStyle.Top;
            Margin = new Padding(0, 0, 0, 12); Padding = Padding.Empty;
            ColumnCount = 2; RowCount = 1;
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            RowStyles.Add(new RowStyle(SizeType.AutoSize));
            foreach (var button in new[] { a, b })
            {
                button.AutoSize = true; button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                button.Padding = new Padding(8, 8, 8, 8); button.Dock = DockStyle.Fill;
            }
            left.Margin = new Padding(0, 0, 6, 0); right.Margin = new Padding(6, 0, 0, 0);
            Controls.Add(left, 0, 0); Controls.Add(right, 1, 0);
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            if (!arranging && left != null && right != null)
            {
                int need = Math.Max(left.GetPreferredSize(Size.Empty).Width, right.GetPreferredSize(Size.Empty).Width);
                bool stack = ClientSize.Width < 2 * (need + LogicalToDeviceUnits(6));
                if (stack != stacked)
                {
                    arranging = true; SuspendLayout(); stacked = stack;
                    ColumnCount = stack ? 1 : 2; RowCount = stack ? 2 : 1;
                    ColumnStyles.Clear(); RowStyles.Clear();
                    ColumnStyles.Add(new ColumnStyle(SizeType.Percent, stack ? 100 : 50));
                    if (!stack) ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                    RowStyles.Add(new RowStyle(SizeType.AutoSize)); if (stack) RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    SetCellPosition(left, new TableLayoutPanelCellPosition(0, 0));
                    SetCellPosition(right, new TableLayoutPanelCellPosition(stack ? 0 : 1, stack ? 1 : 0));
                    int gap = LogicalToDeviceUnits(6);
                    left.Margin = stack ? new Padding(0, 0, 0, gap) : new Padding(0, 0, gap, 0);
                    right.Margin = stack ? Padding.Empty : new Padding(gap, 0, 0, 0);
                    ResumeLayout(false); arranging = false;
                }
            }
            base.OnLayout(e);
        }
    }

    internal static class LayoutChecks
    {
        private static IEnumerable<Control> All(Control parent)
        {
            yield return parent;
            foreach (Control child in parent.Controls) foreach (var descendant in All(child)) yield return descendant;
        }
        internal static void Run(string directory)
        {
            Directory.CreateDirectory(directory);
            var report = new StringBuilder("Layout geometry and text-metric stress tests. DPI/font scaling is simulated; system display settings are not modified.\r\n");
            foreach (float scale in new[] { 3F, 1F, 1.25F, 1.5F, 2F, 2.5F })
            foreach (int width in new[] { 620, 360 })
            {
                using (var form = new MainForm(true))
                {
                    form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000);
                    form.Show(); Application.DoEvents();
                    var controls = All(form).ToList();
                    var fonts = controls.ToDictionary(c => c, c => new Font(c.Font.FontFamily, c.Font.SizeInPoints * scale, c.Font.Style));
                    form.Scale(new SizeF(scale, scale));
                    foreach (Control c in controls) c.Font = fonts[c];
                    form.ClientSize = new Size((int)(width * scale), (int)(520 * scale));
                    form.PerformLayout(); Application.DoEvents(); form.PerformLayout(); Application.DoEvents();
                    string name = "layout-" + (int)(scale * 100) + "-" + width;
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(directory, name + ".png")); }
                    int labels = 0, buttons = 0;
                    foreach (Control c in All(form))
                    {
                        if (c is Label)
                        {
                            Size preferred = c.GetPreferredSize(new Size(c.Width, 0));
                            if (c.Height < preferred.Height) throw new Exception("Clipped label at " + scale + "/" + width + ": " + c.Text);
                            var measured = TextRenderer.MeasureText(c.Text, c.Font, new Size(Math.Max(1, c.Width), Int32.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
                            if (c.Height < measured.Height) throw new Exception("Painted label text exceeds bounds at " + scale + "/" + width + ": " + c.Text);
                            labels++;
                        }
                        if (c is Button)
                        {
                            var required = TextRenderer.MeasureText(c.Text, c.Font, new Size(Math.Max(1, c.Width - c.Padding.Horizontal - 8), Int32.MaxValue), TextFormatFlags.WordBreak);
                            if (c.Height < required.Height + c.Padding.Vertical) throw new Exception("Clipped button at " + scale + "/" + width + ": " + c.Text);
                            buttons++;
                        }
                        var siblings = c.Controls.Cast<Control>().Where(x => x.Visible).ToList();
                        for (int i = 0; i < siblings.Count; i++) for (int j = i + 1; j < siblings.Count; j++)
                            if (siblings[i].Bounds.IntersectsWith(siblings[j].Bounds)) throw new Exception("Overlapping controls at " + name + ": " + siblings[i].Text + " " + siblings[i].Bounds + " preferred=" + siblings[i].GetPreferredSize(Size.Empty) + " / " + siblings[j].Text + " " + siblings[j].Bounds);
                    }
                    report.AppendLine("PASS " + name + ": " + labels + " labels, " + buttons + " buttons; measured text fits; no sibling overlap.");
                    form.Close();
                    foreach (var font in fonts.Values) font.Dispose();
                }
            }
            File.WriteAllText(Path.Combine(directory, "layout-results.txt"), report.ToString(), Encoding.UTF8);
            Console.WriteLine(report);
        }
    }
}
