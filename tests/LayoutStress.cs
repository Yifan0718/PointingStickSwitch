using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using PointingStickSwitch;

internal static class LayoutStress
{
    private static IEnumerable<Control> All(Control parent)
    { yield return parent; foreach (Control c in parent.Controls) foreach (Control x in All(c)) yield return x; }
    [STAThread]
    private static int Main(string[] args)
    {
        var report = new List<string>();
        try
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            foreach (float scale in new[] { 1F, 1.25F, 1.5F, 2F, 2.5F, 3F }) foreach (int width in new[] { 620, 360 })
            {
                using (var form = new MainForm(true))
                {
                    form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-20000, -20000);
                    form.Show(); Application.DoEvents();
                    var controls = All(form).ToList();
                    var fonts = controls.ToDictionary(c => c, c => new Font(c.Font.FontFamily, c.Font.SizeInPoints * scale, c.Font.Style));
                    foreach (var c in controls) c.Font = fonts[c];
                    form.ClientSize = new Size(width, 520); form.PerformLayout(); Application.DoEvents(); form.PerformLayout(); Application.DoEvents();
                    foreach (Control c in All(form))
                    {
                        if ((c is Label || c is RadioButton || c is Button) && c.Height < c.GetPreferredSize(new Size(c.Width, 0)).Height)
                            throw new Exception("Font-only clipping " + scale + "/" + width + ": " + c.Text);
                        var children = c.Controls.Cast<Control>().Where(x => x.Visible).ToList();
                        for (int i = 0; i < children.Count; i++) for (int j = i + 1; j < children.Count; j++)
                            if (children[i].Bounds.IntersectsWith(children[j].Bounds)) throw new Exception("Font-only overlap " + scale + "/" + width);
                    }
                    report.Add("PASS text-only " + (int)(scale * 100) + "% / width " + width + ": labels, radio choices, buttons; no overlap");
                    form.Close(); foreach (var font in fonts.Values) font.Dispose();
                }
            }
            report.Add("RESULT=PASS"); return 0;
        }
        catch (Exception ex) { report.Add("FAIL " + ex); return 1; }
        finally { File.WriteAllLines(args[0], report, Encoding.UTF8); }
    }
}
