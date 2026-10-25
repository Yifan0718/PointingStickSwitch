using System;
using System.Drawing;
using System.Windows.Forms;

namespace PointingStickSwitch
{
    // Native RadioButton captions can paint as one clipped line even when
    // GetPreferredSize reports that they fit. A separate Label owns wrapping.
    internal sealed class DeviceChoice : TableLayoutPanel
    {
        private readonly RadioButton radio;
        private readonly Label caption;
        internal event EventHandler CheckedChanged;
        internal bool Checked { get { return radio.Checked; } set { radio.Checked = value; } }
        internal DeviceChoice(string text)
        {
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Dock = DockStyle.Fill; ColumnCount = 2; RowCount = 1;
            Margin = new Padding(0, 4, 0, 8); Padding = Padding.Empty;
            ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            RowStyles.Add(new RowStyle(SizeType.AutoSize));
            radio = new RadioButton { AutoSize = true, Margin = new Padding(0, 4, 6, 0), AccessibleName = text };
            caption = new Label { Text = text, AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
            radio.CheckedChanged += delegate { if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); };
            caption.Click += delegate { radio.Checked = true; radio.Focus(); };
            Controls.Add(radio, 0, 0); Controls.Add(caption, 1, 0);
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            if (radio != null && caption != null)
                caption.MaximumSize = new Size(Math.Max(1, ClientSize.Width - Padding.Horizontal - radio.GetPreferredSize(Size.Empty).Width - radio.Margin.Horizontal), 0);
            base.OnLayout(e);
        }
        internal void FocusChoice() { radio.Focus(); }
    }
}
