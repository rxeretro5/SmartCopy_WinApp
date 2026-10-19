using System;
using System.Drawing;
using System.Windows.Forms;

namespace QuickCopy
{
    /// <summary>หน้าต่างเพิ่ม/แก้ไขข้อความ</summary>
    internal class EditForm : Form
    {
        private readonly TextBox _title;
        private readonly TextBox _text;
        private readonly Button _ok;

        public string SnippetTitle { get { return _title.Text.Trim(); } }
        public string SnippetText { get { return _text.Text; } }

        public EditForm(string caption, Snippet s, float fontSize)
        {
            Text = caption;
            Font = Theme.UiFont(10f);
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            ClientSize = new Size(Theme.S(440), Theme.S(380));
            MinimumSize = new Size(Theme.S(320), Theme.S(300));
            Icon = Theme.LoadAppIcon(SystemInformation.SmallIconSize);
            Padding = new Padding(Theme.S(10));

            var lblTitle = new Label { Text = "ชื่อสั้นๆ (ไม่บังคับ — ใช้แสดงแทนข้อความยาว)", AutoSize = true, Dock = DockStyle.Top };
            _title = new TextBox { Dock = DockStyle.Top, Text = s != null ? (s.Title ?? "") : "" };
            var spacer1 = new Panel { Height = Theme.S(8), Dock = DockStyle.Top };
            var lblText = new Label { Text = "ข้อความที่จะคัดลอก (กด Enter ขึ้นบรรทัดใหม่ได้)", AutoSize = true, Dock = DockStyle.Top };
            _text = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                AcceptsReturn = true,
                AcceptsTab = false,
                ScrollBars = ScrollBars.Vertical,
                Font = Theme.UiFont(Math.Max(10f, fontSize)),
                Text = s != null ? (s.Text ?? "") : ""
            };

            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Padding = new Padding(0, Theme.S(8), 0, 0)
            };
            var ok = _ok = new Button { Text = "บันทึก", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(Theme.S(90), Theme.S(30)) };
            var cancel = new Button { Text = "ยกเลิก", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(Theme.S(90), Theme.S(30)) };
            var vars = new Button { Text = "แทรกตัวแปรวันที่ ▾", AutoSize = true, MinimumSize = new Size(Theme.S(90), Theme.S(30)) };
            var varMenu = new ContextMenuStrip();
            foreach (var v in Templates.Variables)
            {
                string name = v[0];
                varMenu.Items.Add(name + "   —   " + v[1], null, (o, e) => InsertAtCaret(name));
            }
            vars.Click += (o, e) => varMenu.Show(vars, new Point(0, vars.Height));
            bottom.Controls.Add(ok);
            bottom.Controls.Add(cancel);
            bottom.Controls.Add(vars);

            var hint = new Label
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                ForeColor = Theme.SubText,
                Text = "Ctrl+Enter = บันทึก   •   Esc = ยกเลิก",
                Padding = new Padding(0, Theme.S(4), 0, 0)
            };

            // Dock: ตัวที่ Add ทีหลังจะถูกจัดชิดขอบก่อน
            Controls.Add(_text);
            Controls.Add(lblText);
            Controls.Add(spacer1);
            Controls.Add(_title);
            Controls.Add(lblTitle);
            Controls.Add(hint);
            Controls.Add(bottom);

            AcceptButton = null; // Enter ใช้ขึ้นบรรทัดใหม่ในช่องข้อความ
            CancelButton = cancel;
            ok.Click += (o, e) =>
            {
                if (string.IsNullOrWhiteSpace(_text.Text))
                {
                    MessageBox.Show(this, "กรุณาพิมพ์ข้อความ", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    DialogResult = DialogResult.None;
                    _text.Focus();
                }
            };
            Shown += (o, e) => { _text.Focus(); _text.SelectionStart = _text.TextLength; };
        }

        private void InsertAtCaret(string s)
        {
            _text.SelectedText = s;
            _text.Focus();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Enter))
            {
                _ok.PerformClick();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    internal static class Prompt
    {
        /// <summary>กล่องรับข้อความบรรทัดเดียว คืนค่า null ถ้ากดยกเลิก</summary>
        public static string Show(IWin32Window owner, string caption, string label, string value)
        {
            using (var f = new Form())
            {
                f.Text = caption;
                f.Font = Theme.UiFont(10f);
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.StartPosition = FormStartPosition.CenterScreen;
                f.MinimizeBox = false;
                f.MaximizeBox = false;
                f.ShowInTaskbar = false;
                f.TopMost = true;
                f.ClientSize = new Size(Theme.S(340), Theme.S(120));

                var lbl = new Label { Text = label, AutoSize = true, Location = new Point(Theme.S(12), Theme.S(12)) };
                var tb = new TextBox { Text = value ?? "", Location = new Point(Theme.S(12), Theme.S(38)), Width = Theme.S(316) };
                var ok = new Button { Text = "ตกลง", DialogResult = DialogResult.OK, Size = new Size(Theme.S(90), Theme.S(30)), Location = new Point(Theme.S(140), Theme.S(78)) };
                var cancel = new Button { Text = "ยกเลิก", DialogResult = DialogResult.Cancel, Size = new Size(Theme.S(90), Theme.S(30)), Location = new Point(Theme.S(238), Theme.S(78)) };
                f.Controls.AddRange(new Control[] { lbl, tb, ok, cancel });
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                f.Shown += (o, e) => { tb.Focus(); tb.SelectAll(); };
                if (f.ShowDialog(owner) != DialogResult.OK) return null;
                string r = tb.Text.Trim();
                return r.Length == 0 ? null : r;
            }
        }
    }
}
