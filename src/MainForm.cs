using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace QuickCopy
{
    internal class MainForm : Form, IMessageFilter
    {
        public const string AppTitle = "แผงข้อความด่วน";
        private const string HotkeyText = "Ctrl+Alt+Space";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValue = "QuickCopyPanel";
        private const int HotkeyId = 1;

        private readonly AppData _data;
        private readonly List<int> _view = new List<int>();   // ตำแหน่งในหมวด ของการ์ดแต่ละใบที่แสดงอยู่

        private readonly Panel _header;
        private readonly IconButton _btnPin, _btnMenu, _btnHide;
        private readonly FlatButton _btnGroup;
        private readonly TextBox _search;
        private readonly SnippetListView _list;
        private readonly TextBox _input;
        private readonly IconButton _btnAdd, _btnFromClipboard;
        private readonly Label _status;
        private readonly ToolTip _tip;

        private readonly NotifyIcon _tray;
        private readonly ContextMenuStrip _trayMenu = new ContextMenuStrip();
        private readonly ContextMenuStrip _mainMenu = new ContextMenuStrip();
        private readonly ContextMenuStrip _itemMenu = new ContextMenuStrip();
        private readonly ContextMenuStrip _groupMenu = new ContextMenuStrip();

        private readonly Timer _fgTimer = new Timer { Interval = 250 };
        private readonly Timer _statusTimer = new Timer { Interval = 3500 };
        private readonly Timer _saveTimer = new Timer { Interval = 500 };
        private readonly Timer _pasteTimer = new Timer { Interval = 120 };

        private IntPtr _lastExternal = IntPtr.Zero;
        private Icon _headerIcon;
        private readonly uint _showMsg;
        private bool _exiting;
        private bool _hotkeyOk;
        private bool _layoutReady;

        private AppSettings St { get { return _data.Settings; } }

        private SnippetGroup CurrentGroup { get { return _data.Groups[St.CurrentGroup]; } }

        private int Grip { get { return Theme.S(6); } }

        private string DefaultStatus
        {
            get { return "คลิก = คัดลอก  •  คลิกขวา = เมนู  •  ลาก = จัดลำดับ"; }
        }

        public MainForm()
        {
            _data = Store.Load();
            _showMsg = Native.RegisterWindowMessage("QuickCopyPanel_Show_v1");

            Text = AppTitle;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Back;
            Font = Theme.UiFont(9.5f);
            DoubleBuffered = true;
            KeyPreview = true;
            MinimumSize = new Size(Theme.S(190), Theme.S(260));
            Icon = Theme.LoadAppIcon(SystemInformation.IconSize);
            _tip = new ToolTip { ShowAlways = true };

            // ── แถบหัว (ลากเพื่อย้ายหน้าต่าง) ──
            _header = new Panel { BackColor = Theme.Header, Cursor = Cursors.SizeAll };
            _header.Paint += Header_Paint;
            _header.MouseDown += Header_MouseDown;
            _header.MouseMove += Header_MouseMove;
            _header.MouseDoubleClick += (s, e) => { if (e.Button == MouseButtons.Left) ResetPosition(); };

            _btnHide = new IconButton { Glyph = "", Fallback = "–" };
            _btnMenu = new IconButton { Glyph = "", Fallback = "≡" };
            _btnPin = new IconButton { Glyph = "", Fallback = "▲" };
            _btnHide.Click += (s, e) => HidePanel();
            _btnMenu.Click += (s, e) => { BuildMainMenu(); ShowMenu(_mainMenu, _btnMenu, new Point(0, _btnMenu.Height)); };
            _btnPin.Click += (s, e) => SetTopMost(!St.TopMost);
            _header.Controls.Add(_btnHide);
            _header.Controls.Add(_btnMenu);
            _header.Controls.Add(_btnPin);
            _tip.SetToolTip(_btnHide, "ซ่อนไปที่ System Tray  (เรียกกลับ: " + HotkeyText + ")");
            _tip.SetToolTip(_btnMenu, "เมนู / ตั้งค่า");

            // ── ปุ่มเลือกหมวดหมู่ ──
            _btnGroup = new FlatButton();
            _btnGroup.Click += (s, e) => { BuildGroupMenu(); ShowMenu(_groupMenu, _btnGroup, new Point(0, _btnGroup.Height)); };
            _tip.SetToolTip(_btnGroup, "เลือก / เพิ่ม / เปลี่ยนชื่อหมวดหมู่");

            // ── ช่องค้นหา ──
            _search = new TextBox { BorderStyle = BorderStyle.FixedSingle };
            _search.TextChanged += (s, e) => RefreshList();
            _search.KeyDown += Search_KeyDown;
            _search.MouseDown += ActivateForTyping;

            // ── รายการข้อความ ──
            _list = new SnippetListView { AllowDrop = true };
            _list.ItemClicked += (s, e) => CopyItem(e.Index, false);
            _list.ItemRightClicked += List_ItemRightClicked;
            _list.ItemMoved += List_ItemMoved;
            _list.DragEnter += (s, e) =>
            {
                e.Effect = e.Data.GetDataPresent(DataFormats.UnicodeText) || e.Data.GetDataPresent(DataFormats.Text)
                    ? DragDropEffects.Copy : DragDropEffects.None;
            };
            _list.DragDrop += (s, e) =>
            {
                string t = e.Data.GetData(DataFormats.UnicodeText) as string ?? e.Data.GetData(DataFormats.Text) as string;
                if (!string.IsNullOrWhiteSpace(t)) AddSnippet(t, null);
            };

            // ── ช่องพิมพ์ข้อความใหม่ ──
            _input = new TextBox
            {
                Multiline = true,
                AcceptsReturn = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle,
                WordWrap = true
            };
            _input.KeyDown += Input_KeyDown;
            _input.MouseDown += ActivateForTyping;

            _btnAdd = new IconButton { Glyph = "", Fallback = "+", ForeColor = Color.White, BackColor = Theme.Accent, HoverColor = Theme.HeaderHover, GlyphSize = 11f };
            _btnFromClipboard = new IconButton { Glyph = "", Fallback = "C", ForeColor = Theme.Accent, HoverColor = Theme.CardHover, GlyphSize = 11f };
            _btnAdd.Click += (s, e) => AddFromInput();
            _btnFromClipboard.Click += (s, e) => AddFromClipboard();
            _tip.SetToolTip(_btnAdd, "เพิ่มข้อความ (Enter)");
            _tip.SetToolTip(_btnFromClipboard, "เพิ่มจากคลิปบอร์ด — ข้อความที่คุณ Ctrl+C ไว้จากที่อื่น");

            _status = new Label { ForeColor = Theme.SubText, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Text = DefaultStatus };

            Controls.Add(_header);
            Controls.Add(_btnGroup);
            Controls.Add(_search);
            Controls.Add(_list);
            Controls.Add(_input);
            Controls.Add(_btnAdd);
            Controls.Add(_btnFromClipboard);
            Controls.Add(_status);

            // ── System Tray ──
            _tray = new NotifyIcon
            {
                Icon = Theme.LoadAppIcon(SystemInformation.SmallIconSize),
                Text = AppTitle + " (" + HotkeyText + ")",
                ContextMenuStrip = _trayMenu,
                Visible = true
            };
            _tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) TogglePanel(); };
            _trayMenu.Opening += (s, e) => BuildTrayMenu();
            BuildTrayMenu();

            _fgTimer.Tick += (s, e) => TrackForeground();
            _statusTimer.Tick += (s, e) => { _statusTimer.Stop(); _status.ForeColor = Theme.SubText; _status.Text = DefaultStatus; };
            _saveTimer.Tick += (s, e) => { _saveTimer.Stop(); SaveNow(); };
            _pasteTimer.Tick += PasteTimer_Tick;
            _fgTimer.Start();

            Application.AddMessageFilter(this);

            ApplyFontSize();
            ApplyBounds();
            TopMost = St.TopMost;
            _btnPin.Checked = St.TopMost;
            UpdatePinTip();
            Opacity = St.OpacityPercent / 100.0;

            _layoutReady = true;
            LayoutChildren();
            RefreshList();
        }

        // ════════════════════════ หน้าต่าง / ระบบ ════════════════════════

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                // คลิกที่แผงนี้แล้ว "ไม่แย่งโฟกัส" จากโปรแกรมที่กำลังพิมพ์อยู่ (เช่น Smart Office บนเบราว์เซอร์)
                cp.ExStyle |= Native.WS_EX_NOACTIVATE;
                cp.ClassStyle |= Native.CS_DROPSHADOW;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.TryRoundCorners(Handle);
            _hotkeyOk = Native.RegisterHotKey(Handle, HotkeyId, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, (uint)Keys.Space);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (_hotkeyOk) Native.UnregisterHotKey(Handle, HotkeyId);
            _hotkeyOk = false;
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_NCHITTEST)
            {
                base.WndProc(ref m);
                if ((int)m.Result == Native.HTCLIENT)
                {
                    long lp = m.LParam.ToInt64();
                    var p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                    int hit = EdgeHit(p);
                    if (hit != 0) m.Result = (IntPtr)hit;
                }
                return;
            }
            if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                if (Visible && Native.GetForegroundWindow() == Handle) { ReturnFocus(); HidePanel(); }
                else ShowPanel(true);
                return;
            }
            if (_showMsg != 0 && m.Msg == (int)_showMsg)
            {
                ShowPanel(true);
                return;
            }
            base.WndProc(ref m);
        }

        private int EdgeHit(Point p)
        {
            int g = Grip;
            bool l = p.X < g, r = p.X >= Width - g, t = p.Y < g, b = p.Y >= Height - g;
            if (t && l) return Native.HTTOPLEFT;
            if (t && r) return Native.HTTOPRIGHT;
            if (b && l) return Native.HTBOTTOMLEFT;
            if (b && r) return Native.HTBOTTOMRIGHT;
            if (l) return Native.HTLEFT;
            if (r) return Native.HTRIGHT;
            if (t) return Native.HTTOP;
            if (b) return Native.HTBOTTOM;
            return 0;
        }

        public bool PreFilterMessage(ref Message m)
        {
            // ให้ล้อเมาส์เลื่อนรายการได้เสมอเมื่อเมาส์อยู่เหนือรายการ แม้โฟกัสจะอยู่ที่ช่องพิมพ์
            if (m.Msg == Native.WM_MOUSEWHEEL && _list.IsHandleCreated && m.HWnd != _list.Handle)
            {
                if (_list.RectangleToScreen(_list.ClientRectangle).Contains(Cursor.Position))
                {
                    Native.SendMessage(_list.Handle, m.Msg, m.WParam, m.LParam);
                    return true;
                }
            }
            return false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Theme.Border))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutChildren();
            Invalidate();
        }

        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            RememberBounds();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HidePanel();
                return;
            }
            RememberBounds();
            SaveNow();
            _tray.Visible = false;
            base.OnFormClosing(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.F)) { _search.Focus(); _search.SelectAll(); return true; }
            if (keyData == (Keys.Control | Keys.N)) { AddDetailed(); return true; }
            if (keyData == Keys.Escape)
            {
                if (_search.Focused && _search.TextLength > 0) _search.Clear();
                else ReturnFocus();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void LayoutChildren()
        {
            if (!_layoutReady) return;
            int g = Grip;
            int w = ClientSize.Width, h = ClientSize.Height;
            int headerH = Theme.S(34);
            _header.SetBounds(1, 1, w - 2, headerH);
            int bs = headerH - Theme.S(6);
            int bx = _header.Width - Theme.S(4) - bs;
            foreach (var b in new[] { _btnHide, _btnMenu, _btnPin })
            {
                b.SetBounds(bx, Theme.S(3), bs, bs);
                bx -= bs + Theme.S(2);
            }

            int x = g, cw = w - g * 2, y = headerH + Theme.S(6);
            _btnGroup.SetBounds(x, y, cw, Theme.S(30));
            y += _btnGroup.Height + Theme.S(5);
            _search.SetBounds(x, y, cw, _search.PreferredHeight);
            y += _search.Height + Theme.S(4);

            int statusH = Theme.S(20);
            int lineH = TextRenderer.MeasureText("กขAg", _input.Font).Height;
            int inputH = lineH * 3 + Theme.S(8);
            int bottom = h - g;
            _status.SetBounds(x, bottom - statusH, cw, statusH);
            int inputTop = bottom - statusH - Theme.S(2) - inputH;
            int btnW = Theme.S(34);
            _input.SetBounds(x, inputTop, cw - btnW - Theme.S(4), inputH);
            int half = (inputH - Theme.S(4)) / 2;
            _btnAdd.SetBounds(x + cw - btnW, inputTop, btnW, half);
            _btnFromClipboard.SetBounds(x + cw - btnW, inputTop + half + Theme.S(4), btnW, half);

            // เว้นขอบซ้าย/ขวาไว้เล็กน้อยเพื่อให้ยังลากปรับขนาดหน้าต่างได้
            _list.SetBounds(x - Theme.S(2), y, cw + Theme.S(4), Math.Max(10, inputTop - Theme.S(4) - y));
        }

        private void Header_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            int ic = Theme.S(18);
            int top = (_header.Height - ic) / 2;
            if (_headerIcon == null) _headerIcon = Theme.LoadAppIcon(new Size(ic > 20 ? 32 : 16, ic > 20 ? 32 : 16));
            g.DrawIcon(_headerIcon, new Rectangle(Theme.S(8), top, ic, ic));
            int tx = Theme.S(8) + ic + Theme.S(6);
            int right = _btnPin.Left - Theme.S(4);
            using (var f = Theme.UiFont(10f, FontStyle.Bold))
                TextRenderer.DrawText(g, AppTitle, f, new Rectangle(tx, 0, Math.Max(0, right - tx), _header.Height), Theme.HeaderText,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }

        private int HeaderEdgeHit(Point p)
        {
            // แถบหัวทับขอบบนของหน้าต่าง จึงต้องตรวจขอบเองเพื่อให้ยังย่อ/ขยายจากขอบบนได้
            return EdgeHit(new Point(p.X + _header.Left, p.Y + _header.Top));
        }

        private void Header_MouseMove(object sender, MouseEventArgs e)
        {
            int hit = HeaderEdgeHit(e.Location);
            Cursor c = Cursors.SizeAll;
            if (hit == Native.HTTOP) c = Cursors.SizeNS;
            else if (hit == Native.HTTOPLEFT) c = Cursors.SizeNWSE;
            else if (hit == Native.HTTOPRIGHT) c = Cursors.SizeNESW;
            else if (hit == Native.HTLEFT || hit == Native.HTRIGHT) c = Cursors.SizeWE;
            if (_header.Cursor != c) _header.Cursor = c;
        }

        private void Header_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || e.Clicks > 1) return;
            int hit = HeaderEdgeHit(e.Location);
            Native.ReleaseCapture();
            Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)(hit != 0 ? hit : Native.HTCAPTION), IntPtr.Zero);
        }

        private void ActivateForTyping(object sender, MouseEventArgs e)
        {
            // แผงนี้ไม่รับโฟกัสโดยอัตโนมัติ เมื่อคลิกช่องพิมพ์จึงต้องเปิดรับคีย์บอร์ดเอง
            if (Native.GetForegroundWindow() != Handle)
            {
                Activate();
                ((Control)sender).Focus();
            }
        }

        private void ShowMenu(ContextMenuStrip menu, Control owner, Point location)
        {
            // เมนูจะปิดเองได้ถูกต้องเมื่อคลิกที่อื่น ก็ต่อเมื่อหน้าต่างนี้ active อยู่
            // (หลังเลือกคำสั่งคัดลอก โปรแกรมจะคืนโฟกัสให้หน้าต่างเดิมเอง)
            if (Native.GetForegroundWindow() != Handle) Activate();
            menu.Show(owner, location);
        }

        private void TrackForeground()
        {
            IntPtr h = Native.GetForegroundWindow();
            if (h != IntPtr.Zero && !Native.IsOwnWindow(h))
                _lastExternal = h;
        }

        /// <summary>คืนโฟกัสให้หน้าต่างที่ใช้งานอยู่ก่อนหน้า (เช่น เบราว์เซอร์ Smart Office)</summary>
        private bool ReturnFocus()
        {
            if (_lastExternal == IntPtr.Zero || !Native.IsWindow(_lastExternal)) return false;
            if (Native.IsIconic(_lastExternal)) Native.ShowWindow(_lastExternal, Native.SW_RESTORE);
            return Native.SetForegroundWindow(_lastExternal);
        }

        // ════════════════════════ แสดง/ซ่อน/ตำแหน่ง ════════════════════════

        private void ShowPanel(bool activate)
        {
            if (!Visible) Show();
            EnsureOnScreen();
            if (St.TopMost) { TopMost = false; TopMost = true; }
            else BringToFront();
            if (activate)
            {
                Activate();
                _search.Focus();
                _search.SelectAll();
            }
        }

        private void HidePanel()
        {
            Hide();
            if (!St.TrayTipShown)
            {
                St.TrayTipShown = true;
                ScheduleSave();
                _tray.ShowBalloonTip(5000, AppTitle,
                    "โปรแกรมยังทำงานอยู่ที่ System Tray (มุมขวาล่าง)\nคลิกไอคอน หรือกด " + HotkeyText + " เพื่อเปิดอีกครั้ง",
                    ToolTipIcon.Info);
            }
        }

        private void TogglePanel()
        {
            if (Visible) HidePanel();
            else ShowPanel(false);
        }

        private Rectangle DefaultBounds()
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            int w = Math.Min(Theme.S((int)(2.9 * 96)), wa.Width / 2);   // กว้างไม่เกิน 3 นิ้ว
            int h = wa.Height * 2 / 3;                                   // สูง 2/3 ของจอ
            int x = wa.Right - w - Theme.S(8);                           // ชิดขวา
            int y = wa.Top + (wa.Height - h) / 2;                        // กึ่งกลางแนวตั้ง
            return new Rectangle(x, y, w, h);
        }

        private void ApplyBounds()
        {
            Rectangle r = St.HasBounds ? new Rectangle(St.WinX, St.WinY, St.WinW, St.WinH) : DefaultBounds();
            if (!IsVisibleOnAnyScreen(r)) r = DefaultBounds();
            Bounds = r;
        }

        private static bool IsVisibleOnAnyScreen(Rectangle r)
        {
            foreach (var s in Screen.AllScreens)
            {
                var i = Rectangle.Intersect(s.WorkingArea, r);
                if (i.Width >= 60 && i.Height >= 60) return true;
            }
            return false;
        }

        private void EnsureOnScreen()
        {
            if (!IsVisibleOnAnyScreen(Bounds)) Bounds = DefaultBounds();
        }

        private void ResetPosition()
        {
            Bounds = DefaultBounds();
            RememberBounds();
        }

        private void RememberBounds()
        {
            if (WindowState != FormWindowState.Normal) return;
            St.WinX = Left; St.WinY = Top; St.WinW = Width; St.WinH = Height;
            ScheduleSave();
        }

        private void SetTopMost(bool on)
        {
            St.TopMost = on;
            TopMost = on;
            _btnPin.Checked = on;
            UpdatePinTip();
            ScheduleSave();
            SetStatus(on ? "แผงจะอยู่เหนือหน้าต่างอื่นเสมอ" : "แผงจะไม่บังหน้าต่างอื่นแล้ว", false);
        }

        private void UpdatePinTip()
        {
            _tip.SetToolTip(_btnPin, St.TopMost ? "อยู่เหนือหน้าต่างอื่น: เปิด (คลิกเพื่อปิด)" : "อยู่เหนือหน้าต่างอื่น: ปิด (คลิกเพื่อเปิด)");
        }

        private void ApplyFontSize()
        {
            _list.SetFontSize(St.FontSize);
            var f = Theme.UiFont(St.FontSize);
            _input.Font = f;
            _search.Font = Theme.UiFont(St.FontSize);
            _btnGroup.Font = Theme.UiFont(St.FontSize, FontStyle.Bold);
            if (IsHandleCreated) SetCueBanners();
            LayoutChildren();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            SetCueBanners();
        }

        private void SetCueBanners()
        {
            Native.SetCueBanner(_search.Handle, "ค้นหา…  (Ctrl+F)");
            // ช่อง Multiline ไม่แสดง cue banner จึงใช้ ToolTip แทน
            _tip.SetToolTip(_input, "พิมพ์ข้อความแล้วกด Enter เพื่อเพิ่ม\nShift+Enter = ขึ้นบรรทัดใหม่");
        }

        // ════════════════════════ รายการข้อความ ════════════════════════

        private void RefreshList()
        {
            var g = CurrentGroup;
            string q = _search.Text.Trim();
            _view.Clear();
            var items = new List<Snippet>();
            for (int i = 0; i < g.Items.Count; i++)
            {
                var s = g.Items[i];
                if (q.Length == 0 ||
                    (s.Text != null && s.Text.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0) ||
                    (s.Title != null && s.Title.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0))
                {
                    _view.Add(i);
                    items.Add(s);
                }
            }
            _list.AllowReorder = q.Length == 0;
            _list.EmptyText = q.Length > 0
                ? "ไม่พบข้อความที่ค้นหา"
                : "ยังไม่มีข้อความในหมวดนี้\n\nพิมพ์ข้อความในช่องด้านล่าง\nแล้วกด Enter เพื่อเพิ่ม\n\nหรือลากข้อความจากโปรแกรมอื่นมาวางตรงนี้";
            _list.SetItems(items);
            _btnGroup.Text = g.Name + "  (" + g.Items.Count + ")";
        }

        private Snippet ItemAt(int viewIndex)
        {
            if (viewIndex < 0 || viewIndex >= _view.Count) return null;
            return CurrentGroup.Items[_view[viewIndex]];
        }

        private void CopyItem(int viewIndex, bool forcePaste)
        {
            var s = ItemAt(viewIndex);
            if (s == null) return;
            string text = Templates.Expand(s.Text);
            if (!SetClipboard(text))
            {
                SetStatus("คัดลอกไม่สำเร็จ คลิปบอร์ดถูกโปรแกรมอื่นใช้อยู่ ลองใหม่อีกครั้ง", true);
                return;
            }

            bool paste = forcePaste || St.AutoPaste;
            bool weHaveFocus = Native.IsOwnWindow(Native.GetForegroundWindow());
            bool focusReturned = true;
            if (weHaveFocus) focusReturned = ReturnFocus();

            _list.Flash(viewIndex, paste && focusReturned ? "วางแล้ว ✓" : "คัดลอกแล้ว ✓");
            if (paste && focusReturned)
            {
                _pasteTimer.Stop();
                _pasteTimer.Start();
                SetStatus("✓ วางข้อความแล้ว", false);
            }
            else
            {
                SetStatus("✓ คัดลอกแล้ว — กด Ctrl+V ในช่องที่ต้องการวาง", false);
            }

            if (St.HideAfterCopy) Hide();
        }

        private void PasteTimer_Tick(object sender, EventArgs e)
        {
            _pasteTimer.Stop();
            IntPtr fg = Native.GetForegroundWindow();
            if (fg == IntPtr.Zero || Native.IsOwnWindow(fg))
            {
                SetStatus("✓ คัดลอกแล้ว — คลิกช่องที่ต้องการแล้วกด Ctrl+V", false);
                return;
            }
            Native.SendCtrlV();
        }

        private static bool SetClipboard(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            try
            {
                Clipboard.SetDataObject(text, true, 10, 50);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void AddSnippet(string text, string title)
        {
            text = (text ?? "").TrimEnd('\r', '\n', ' ', '\t');
            if (text.Trim().Length == 0) return;
            if (_search.TextLength > 0) _search.Clear();

            var g = CurrentGroup;
            int dup = g.Items.FindIndex(x => x.Text == text);
            if (dup >= 0)
            {
                RefreshList();
                int vi = _view.IndexOf(dup);
                _list.EnsureVisible(vi);
                _list.Flash(vi, "มีอยู่แล้ว");
                SetStatus("ข้อความนี้มีอยู่ในหมวดแล้ว", false);
                return;
            }
            g.Items.Add(new Snippet(string.IsNullOrWhiteSpace(title) ? null : title.Trim(), text));
            RefreshList();
            _list.ScrollToBottom();
            _list.Flash(_view.Count - 1, "เพิ่มแล้ว");
            SetStatus("✓ เพิ่มข้อความแล้ว", false);
            ScheduleSave();
        }

        private void AddFromInput()
        {
            string t = _input.Text;
            if (string.IsNullOrWhiteSpace(t)) { _input.Focus(); return; }
            AddSnippet(t, null);
            _input.Clear();
        }

        private void AddFromClipboard()
        {
            string t = null;
            try { if (Clipboard.ContainsText()) t = Clipboard.GetText(); }
            catch { }
            if (string.IsNullOrWhiteSpace(t))
            {
                SetStatus("คลิปบอร์ดว่าง — ลอง Ctrl+C ข้อความจากที่อื่นก่อน", true);
                return;
            }
            AddSnippet(t, null);
        }

        private void AddDetailed()
        {
            using (var f = new EditForm("เพิ่มข้อความ", null, St.FontSize))
            {
                if (f.ShowDialog(this) == DialogResult.OK)
                    AddSnippet(f.SnippetText, f.SnippetTitle);
            }
        }

        private void EditItem(int viewIndex)
        {
            var s = ItemAt(viewIndex);
            if (s == null) return;
            using (var f = new EditForm("แก้ไขข้อความ", s, St.FontSize))
            {
                if (f.ShowDialog(this) == DialogResult.OK)
                {
                    s.Text = f.SnippetText.TrimEnd('\r', '\n');
                    s.Title = string.IsNullOrWhiteSpace(f.SnippetTitle) ? null : f.SnippetTitle;
                    RefreshList();
                    ScheduleSave();
                }
            }
        }

        private void DeleteItem(int viewIndex)
        {
            var s = ItemAt(viewIndex);
            if (s == null) return;
            string preview = s.DisplayTitle ?? s.Text;
            if (preview.Length > 80) preview = preview.Substring(0, 80) + "…";
            if (MessageBox.Show(this, "ลบข้อความนี้?\n\n" + preview, AppTitle, MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            CurrentGroup.Items.RemoveAt(_view[viewIndex]);
            RefreshList();
            ScheduleSave();
        }

        private void MoveItem(int from, int to)
        {
            var items = CurrentGroup.Items;
            if (from < 0 || from >= items.Count) return;
            to = Math.Max(0, Math.Min(items.Count - 1, to));
            if (from == to) return;
            var s = items[from];
            items.RemoveAt(from);
            items.Insert(to, s);
            RefreshList();
            ScheduleSave();
        }

        private void List_ItemMoved(object sender, ItemMovedEventArgs e)
        {
            // ลากได้เฉพาะตอนไม่ค้นหา จึงตำแหน่งในมุมมอง = ตำแหน่งในหมวด
            MoveItem(e.From, e.To);
            _list.Flash(e.To, "ย้ายแล้ว");
        }

        private void List_ItemRightClicked(object sender, ItemEventArgs e)
        {
            _itemMenu.Items.Clear();
            int vi = e.Index;
            var s = ItemAt(vi);
            if (s != null)
            {
                int real = _view[vi];
                _itemMenu.Items.Add("คัดลอก", null, (o, a) => CopyItem(vi, false));
                _itemMenu.Items.Add("คัดลอก และวางในหน้าต่างล่าสุดทันที", null, (o, a) => CopyItem(vi, true));
                _itemMenu.Items.Add(new ToolStripSeparator());
                _itemMenu.Items.Add("แก้ไข…", null, (o, a) => EditItem(vi));
                _itemMenu.Items.Add("ทำสำเนา", null, (o, a) =>
                {
                    CurrentGroup.Items.Insert(real + 1, s.Clone());
                    RefreshList();
                    ScheduleSave();
                });
                bool canMove = _search.TextLength == 0;
                var top = new ToolStripMenuItem("ย้ายไปบนสุด", null, (o, a) => MoveItem(real, 0)) { Enabled = canMove && real > 0 };
                var up = new ToolStripMenuItem("เลื่อนขึ้น", null, (o, a) => MoveItem(real, real - 1)) { Enabled = canMove && real > 0 };
                var down = new ToolStripMenuItem("เลื่อนลง", null, (o, a) => MoveItem(real, real + 1)) { Enabled = canMove && real < CurrentGroup.Items.Count - 1 };
                _itemMenu.Items.Add(top);
                _itemMenu.Items.Add(up);
                _itemMenu.Items.Add(down);

                var moveTo = new ToolStripMenuItem("ย้ายไปหมวด");
                for (int gi = 0; gi < _data.Groups.Count; gi++)
                {
                    if (gi == St.CurrentGroup) continue;
                    int target = gi;
                    moveTo.DropDownItems.Add(_data.Groups[gi].Name, null, (o, a) =>
                    {
                        CurrentGroup.Items.Remove(s);
                        _data.Groups[target].Items.Add(s);
                        RefreshList();
                        ScheduleSave();
                        SetStatus("ย้ายไปหมวด \"" + _data.Groups[target].Name + "\" แล้ว", false);
                    });
                }
                moveTo.Enabled = moveTo.DropDownItems.Count > 0;
                _itemMenu.Items.Add(moveTo);
                _itemMenu.Items.Add(new ToolStripSeparator());
                var del = new ToolStripMenuItem("ลบ", null, (o, a) => DeleteItem(vi)) { ForeColor = Color.FromArgb(190, 30, 30) };
                _itemMenu.Items.Add(del);
            }
            else
            {
                _itemMenu.Items.Add("เพิ่มข้อความ (แบบละเอียด)…", null, (o, a) => AddDetailed());
                _itemMenu.Items.Add("เพิ่มจากคลิปบอร์ด", null, (o, a) => AddFromClipboard());
            }
            ShowMenu(_itemMenu, _list, e.Location);
        }

        private void Search_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                if (_view.Count > 0) CopyItem(0, false);
            }
            else if (e.KeyCode == Keys.Down)
            {
                e.SuppressKeyPress = true;
                _input.Focus();
            }
        }

        private void Input_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !e.Shift && !e.Control)
            {
                e.SuppressKeyPress = true;
                AddFromInput();
            }
            else if (e.KeyCode == Keys.A && e.Control)
            {
                e.SuppressKeyPress = true;
                _input.SelectAll();
            }
        }

        // ════════════════════════ หมวดหมู่ ════════════════════════

        private void SelectGroup(int index)
        {
            if (index < 0 || index >= _data.Groups.Count) return;
            St.CurrentGroup = index;
            _search.Clear();
            RefreshList();
            ScheduleSave();
        }

        private void BuildGroupMenu()
        {
            _groupMenu.Items.Clear();
            for (int i = 0; i < _data.Groups.Count; i++)
            {
                int gi = i;
                var g = _data.Groups[i];
                var mi = new ToolStripMenuItem(g.Name + "  (" + g.Items.Count + ")", null, (o, a) => SelectGroup(gi)) { Checked = i == St.CurrentGroup };
                _groupMenu.Items.Add(mi);
            }
            _groupMenu.Items.Add(new ToolStripSeparator());
            _groupMenu.Items.Add("＋ เพิ่มหมวดหมู่ใหม่…", null, (o, a) => AddGroup());
            _groupMenu.Items.Add("เปลี่ยนชื่อหมวดนี้…", null, (o, a) => RenameGroup());
            var up = new ToolStripMenuItem("เลื่อนหมวดนี้ขึ้น", null, (o, a) => MoveGroup(-1)) { Enabled = St.CurrentGroup > 0 };
            var down = new ToolStripMenuItem("เลื่อนหมวดนี้ลง", null, (o, a) => MoveGroup(1)) { Enabled = St.CurrentGroup < _data.Groups.Count - 1 };
            _groupMenu.Items.Add(up);
            _groupMenu.Items.Add(down);
            var del = new ToolStripMenuItem("ลบหมวดนี้…", null, (o, a) => DeleteGroup()) { Enabled = _data.Groups.Count > 1 };
            _groupMenu.Items.Add(del);
        }

        private void AddGroup()
        {
            string name = Prompt.Show(this, "เพิ่มหมวดหมู่", "ชื่อหมวดหมู่ใหม่:", "");
            if (name == null) return;
            _data.Groups.Add(new SnippetGroup(name));
            SelectGroup(_data.Groups.Count - 1);
        }

        private void RenameGroup()
        {
            string name = Prompt.Show(this, "เปลี่ยนชื่อหมวดหมู่", "ชื่อใหม่:", CurrentGroup.Name);
            if (name == null) return;
            CurrentGroup.Name = name;
            RefreshList();
            ScheduleSave();
        }

        private void MoveGroup(int delta)
        {
            int i = St.CurrentGroup, j = i + delta;
            if (j < 0 || j >= _data.Groups.Count) return;
            var g = _data.Groups[i];
            _data.Groups.RemoveAt(i);
            _data.Groups.Insert(j, g);
            SelectGroup(j);
        }

        private void DeleteGroup()
        {
            if (_data.Groups.Count <= 1) return;
            var g = CurrentGroup;
            if (MessageBox.Show(this, "ลบหมวด \"" + g.Name + "\" และข้อความทั้งหมด " + g.Items.Count + " รายการ?",
                    AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            _data.Groups.RemoveAt(St.CurrentGroup);
            SelectGroup(Math.Max(0, St.CurrentGroup - 1));
        }

        // ════════════════════════ เมนู ════════════════════════

        private ToolStripMenuItem Check(string text, bool on, Action action)
        {
            return new ToolStripMenuItem(text, null, (o, a) => action()) { Checked = on };
        }

        private void BuildMainMenu()
        {
            var m = _mainMenu.Items;
            m.Clear();
            m.Add("เพิ่มข้อความ (แบบละเอียด)…   Ctrl+N", null, (o, a) => AddDetailed());
            m.Add("เพิ่มจากคลิปบอร์ด", null, (o, a) => AddFromClipboard());
            m.Add(new ToolStripSeparator());
            m.Add(Check("อยู่เหนือหน้าต่างอื่นเสมอ", St.TopMost, () => SetTopMost(!St.TopMost)));
            m.Add(Check("คลิกแล้ววางให้ทันที (ไม่ต้องกด Ctrl+V)", St.AutoPaste, () =>
            {
                St.AutoPaste = !St.AutoPaste;
                ScheduleSave();
                SetStatus(St.AutoPaste ? "โหมดวางอัตโนมัติ: คลิกข้อความ = วางในช่องที่เคอร์เซอร์อยู่ทันที" : "โหมดคัดลอก: คลิกข้อความแล้วกด Ctrl+V เอง", false);
            }));
            m.Add(Check("ซ่อนแผงหลังคัดลอก", St.HideAfterCopy, () => { St.HideAfterCopy = !St.HideAfterCopy; ScheduleSave(); }));

            var fontMenu = new ToolStripMenuItem("ขนาดตัวอักษร");
            foreach (var pair in new[] { new { N = "เล็ก", V = 9f }, new { N = "ปกติ", V = 10f }, new { N = "ใหญ่", V = 11.5f }, new { N = "ใหญ่มาก", V = 13f } })
            {
                float v = pair.V;
                fontMenu.DropDownItems.Add(Check(pair.N, Math.Abs(St.FontSize - v) < 0.1f, () => { St.FontSize = v; ApplyFontSize(); RefreshList(); ScheduleSave(); }));
            }
            m.Add(fontMenu);

            var opMenu = new ToolStripMenuItem("ความทึบของแผง");
            foreach (int pct in new[] { 100, 90, 80, 70, 60 })
            {
                int p = pct;
                opMenu.DropDownItems.Add(Check(p + "%", St.OpacityPercent == p, () => { St.OpacityPercent = p; Opacity = p / 100.0; ScheduleSave(); }));
            }
            m.Add(opMenu);
            m.Add(new ToolStripSeparator());

            var dataMenu = new ToolStripMenuItem("ข้อมูล");
            dataMenu.DropDownItems.Add("สำรองข้อมูล (ส่งออกไฟล์)…", null, (o, a) => ExportData());
            dataMenu.DropDownItems.Add("นำเข้าข้อมูล (.json / .txt)…", null, (o, a) => ImportData());
            dataMenu.DropDownItems.Add("เปิดโฟลเดอร์ที่เก็บข้อมูล", null, (o, a) =>
            {
                SaveNow();
                try { Process.Start("explorer.exe", "/select,\"" + Store.DataPath + "\""); } catch { }
            });
            m.Add(dataMenu);
            m.Add(Check("เริ่มทำงานพร้อม Windows", IsStartupEnabled(), () => SetStartup(!IsStartupEnabled())));
            m.Add("จัดตำแหน่งกลับค่าเริ่มต้น (ชิดขวา)", null, (o, a) => ResetPosition());
            m.Add("วิธีใช้ / ตัวแปรวันที่", null, (o, a) => ShowHelp());
            m.Add(new ToolStripSeparator());
            m.Add("ซ่อนไปที่ System Tray", null, (o, a) => HidePanel());
            m.Add("ออกจากโปรแกรม", null, (o, a) => ExitApp());
        }

        private void BuildTrayMenu()
        {
            var m = _trayMenu.Items;
            m.Clear();
            var show = new ToolStripMenuItem(Visible ? "ซ่อนแผง" : "แสดงแผง", null, (o, a) => TogglePanel());
            show.Font = new Font(show.Font, FontStyle.Bold);
            m.Add(show);
            m.Add(Check("อยู่เหนือหน้าต่างอื่นเสมอ", St.TopMost, () => SetTopMost(!St.TopMost)));
            m.Add(Check("คลิกแล้ววางให้ทันที", St.AutoPaste, () => { St.AutoPaste = !St.AutoPaste; ScheduleSave(); }));
            m.Add(Check("เริ่มทำงานพร้อม Windows", IsStartupEnabled(), () => SetStartup(!IsStartupEnabled())));
            m.Add("จัดตำแหน่งกลับค่าเริ่มต้น", null, (o, a) => { ResetPosition(); ShowPanel(false); });
            m.Add(new ToolStripSeparator());
            m.Add("ออกจากโปรแกรม", null, (o, a) => ExitApp());
        }

        private void ShowHelp()
        {
            var vars = string.Join("\n", Templates.Variables.Select(v => "   " + v[0] + "   →   " + Templates.Expand(v[0])).ToArray());
            string msg =
                "วิธีใช้\n" +
                "• พิมพ์ข้อความในช่องด้านล่าง แล้วกด Enter เพื่อเพิ่ม (Shift+Enter = ขึ้นบรรทัดใหม่)\n" +
                "• คลิกที่ข้อความ = คัดลอก แล้วไปกด Ctrl+V ในช่องที่ต้องการ\n" +
                "   (คลิกที่แผงนี้จะไม่แย่งเคอร์เซอร์จาก Smart Office จึงกด Ctrl+V ได้ทันที)\n" +
                "• เปิดเมนู ≡ → \"คลิกแล้ววางให้ทันที\" เพื่อให้คลิกครั้งเดียวแล้ววางเลย\n" +
                "• คลิกขวาที่ข้อความ = แก้ไข / ลบ / ย้ายหมวด\n" +
                "• ลากการ์ดขึ้น-ลง = จัดลำดับ\n" +
                "• ลากแถบสีน้ำเงินด้านบน = ย้ายตำแหน่งแผง  •  ลากขอบ = ปรับขนาด\n" +
                "• ดับเบิลคลิกแถบสีน้ำเงิน = กลับไปชิดขวาเหมือนเดิม\n" +
                "• " + HotkeyText + " = เปิดแผงและค้นหา → พิมพ์คำค้น → Enter = คัดลอกรายการแรก\n" +
                (_hotkeyOk ? "" : "   (คีย์ลัดนี้ถูกโปรแกรมอื่นใช้อยู่ จึงใช้ไม่ได้ในขณะนี้)\n") +
                "\nตัวแปรพิเศษ (แทนค่าอัตโนมัติตอนคัดลอก)\n" + vars +
                "\n\nที่เก็บข้อมูล:\n" + Store.DataPath;
            MessageBox.Show(this, msg, AppTitle + " — วิธีใช้", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ════════════════════════ ข้อมูล / ตั้งค่า ════════════════════════

        private void ScheduleSave()
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void SaveNow()
        {
            _saveTimer.Stop();
            try
            {
                Store.Save(_data);
            }
            catch (Exception ex)
            {
                SetStatus("บันทึกข้อมูลไม่สำเร็จ: " + ex.Message, true);
            }
        }

        private void ExportData()
        {
            SaveNow();
            using (var d = new SaveFileDialog
            {
                Filter = "ไฟล์ข้อมูล (*.json)|*.json",
                FileName = "แผงข้อความด่วน-สำรอง-" + DateTime.Now.ToString("yyyyMMdd") + ".json"
            })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    Store.WriteTo(d.FileName, _data);
                    SetStatus("✓ สำรองข้อมูลแล้ว", false);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "บันทึกไฟล์ไม่สำเร็จ\n" + ex.Message, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ImportData()
        {
            using (var d = new OpenFileDialog { Filter = "ไฟล์ที่รองรับ (*.json;*.txt)|*.json;*.txt|ทุกไฟล์ (*.*)|*.*" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    if (Path.GetExtension(d.FileName).Equals(".txt", StringComparison.OrdinalIgnoreCase))
                        ImportText(d.FileName);
                    else
                        ImportJson(d.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "นำเข้าไม่สำเร็จ\n" + ex.Message, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ImportText(string path)
        {
            // ไฟล์ข้อความ: 1 บรรทัด = 1 รายการ เข้าหมวดปัจจุบัน
            var lines = File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            var g = CurrentGroup;
            int added = 0;
            foreach (var l in lines)
            {
                if (g.Items.Any(x => x.Text == l)) continue;
                g.Items.Add(new Snippet(null, l));
                added++;
            }
            RefreshList();
            ScheduleSave();
            SetStatus("✓ นำเข้า " + added + " รายการ เข้าหมวด \"" + g.Name + "\"", false);
        }

        private void ImportJson(string path)
        {
            var imported = Store.TryRead(path);
            if (imported == null || imported.Groups == null) throw new InvalidDataException("รูปแบบไฟล์ไม่ถูกต้อง");
            imported.Normalize();
            var r = MessageBox.Show(this,
                "พบ " + imported.Groups.Count + " หมวด, " + imported.Groups.Sum(x => x.Items.Count) + " รายการ\n\n" +
                "Yes = รวมกับข้อมูลเดิม (ข้อความซ้ำจะไม่ถูกเพิ่ม)\nNo = แทนที่ข้อมูลเดิมทั้งหมด\nCancel = ยกเลิก",
                AppTitle, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return;
            if (r == DialogResult.No)
            {
                _data.Groups.Clear();
                _data.Groups.AddRange(imported.Groups);
                St.CurrentGroup = 0;
            }
            else
            {
                foreach (var ig in imported.Groups)
                {
                    var g = _data.Groups.FirstOrDefault(x => x.Name == ig.Name);
                    if (g == null) { _data.Groups.Add(ig); continue; }
                    foreach (var s in ig.Items)
                        if (!g.Items.Any(x => x.Text == s.Text)) g.Items.Add(s);
                }
            }
            RefreshList();
            SaveNow();
            SetStatus("✓ นำเข้าข้อมูลแล้ว", false);
        }

        private static string StartupCommand
        {
            get { return "\"" + Application.ExecutablePath + "\""; }
        }

        private static bool IsStartupEnabled()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    var v = k == null ? null : k.GetValue(RunValue) as string;
                    return v != null && v.Equals(StartupCommand, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { return false; }
        }

        private void SetStartup(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue(RunValue, StartupCommand);
                    else k.DeleteValue(RunValue, false);
                }
                SetStatus(on ? "✓ จะเปิดโปรแกรมอัตโนมัติเมื่อเปิดเครื่อง" : "ยกเลิกการเปิดพร้อม Windows แล้ว", false);
            }
            catch (Exception ex)
            {
                SetStatus("ตั้งค่าไม่สำเร็จ: " + ex.Message, true);
            }
        }

        private void SetStatus(string text, bool warn)
        {
            _status.Text = text;
            _status.ForeColor = warn ? Color.FromArgb(190, 60, 20) : Theme.SuccessText;
            _statusTimer.Stop();
            _statusTimer.Start();
        }

        private void ExitApp()
        {
            _exiting = true;
            Application.RemoveMessageFilter(this);
            Close();
            Application.Exit();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _tray.Dispose();
                _fgTimer.Dispose();
                _statusTimer.Dispose();
                _saveTimer.Dispose();
                _pasteTimer.Dispose();
                _tip.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>ปุ่มเลือกหมวดหมู่แบบแบน: ชื่อหมวด + ลูกศร ▾</summary>
    internal class FlatButton : Control
    {
        private bool _hover;

        public FlatButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            Cursor = Cursors.Hand;
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Back);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var p = Theme.RoundRect(r, Theme.S(5)))
            {
                using (var b = new SolidBrush(_hover ? Theme.CardHover : Theme.Card)) g.FillPath(b, p);
                using (var pen = new Pen(_hover ? Theme.CardHoverBorder : Theme.CardBorder)) g.DrawPath(pen, p);
            }
            int arrowW = Theme.S(22);
            int iconW = 0;
            if (Theme.HasIconFont)
            {
                iconW = Theme.S(24);
                using (var f = new Font(Theme.IconFontName, 10f))
                    TextRenderer.DrawText(g, "", f, new Rectangle(Theme.S(4), 0, iconW, Height), Theme.Accent,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            var tr = new Rectangle(Theme.S(6) + iconW, 0, Width - arrowW - iconW - Theme.S(8), Height);
            TextRenderer.DrawText(g, Text, Font, tr, Theme.Accent,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, "▾", Font, new Rectangle(Width - arrowW, 0, arrowW - Theme.S(4), Height), Theme.SubText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }
}
