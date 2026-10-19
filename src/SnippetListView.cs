using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace QuickCopy
{
    internal class ItemEventArgs : EventArgs
    {
        public int Index;
        public Point Location;
        public ItemEventArgs(int index, Point location) { Index = index; Location = location; }
    }

    internal class ItemMovedEventArgs : EventArgs
    {
        public int From, To;
        public ItemMovedEventArgs(int from, int to) { From = from; To = to; }
    }

    /// <summary>
    /// รายการข้อความแบบการ์ด: คลิกซ้าย = เลือก(คัดลอก), คลิกขวา = เมนู, ลาก = จัดลำดับ
    /// </summary>
    internal class SnippetListView : Control
    {
        private const int MaxPreviewLines = 4;

        private List<Snippet> _items = new List<Snippet>();
        private readonly List<Rectangle> _bounds = new List<Rectangle>();
        private readonly VScrollBar _vbar;
        private readonly Timer _flashTimer;
        private readonly ToolTip _tip;
        private int _scrollY;
        private int _contentHeight;
        private int _hover = -1;
        private int _flash = -1;
        private string _flashText;
        private int _tipIndex = -1;

        // ลากจัดลำดับ
        private int _downIndex = -1;
        private Point _downPoint;
        private bool _dragging;
        private int _dropIndex = -1;

        private Font _titleFont, _textFont, _smallFont;

        public bool AllowReorder = true;
        public string EmptyText = "";

        public event EventHandler<ItemEventArgs> ItemClicked;
        public event EventHandler<ItemEventArgs> ItemRightClicked;
        public event EventHandler<ItemMovedEventArgs> ItemMoved;

        public SnippetListView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = Theme.Back;

            _vbar = new VScrollBar { Dock = DockStyle.Right, Visible = false, Width = SystemInformation.VerticalScrollBarWidth };
            _vbar.Scroll += (s, e) => { _scrollY = _vbar.Value; Invalidate(); };
            _vbar.ValueChanged += (s, e) => { _scrollY = _vbar.Value; Invalidate(); };
            Controls.Add(_vbar);

            _flashTimer = new Timer { Interval = 900 };
            _flashTimer.Tick += (s, e) => { _flashTimer.Stop(); _flash = -1; Invalidate(); };

            _tip = new ToolTip { ShowAlways = true, InitialDelay = 700, AutoPopDelay = 15000, ReshowDelay = 200 };

            SetFontSize(10f);
        }

        public void SetFontSize(float size)
        {
            if (_titleFont != null) { _titleFont.Dispose(); _textFont.Dispose(); _smallFont.Dispose(); }
            _titleFont = Theme.UiFont(size, FontStyle.Bold);
            _textFont = Theme.UiFont(size);
            _smallFont = Theme.UiFont(Math.Max(7.5f, size - 1.5f));
            Relayout();
        }

        public void SetItems(List<Snippet> items)
        {
            _items = items ?? new List<Snippet>();
            _hover = -1;
            _flash = -1;
            _tipIndex = -1;
            _tip.SetToolTip(this, null);
            Relayout();
        }

        public int Count { get { return _items.Count; } }

        public void Flash(int index, string text)
        {
            _flash = index;
            _flashText = text;
            _flashTimer.Stop();
            _flashTimer.Start();
            Invalidate();
        }

        public void EnsureVisible(int index)
        {
            if (index < 0 || index >= _bounds.Count) return;
            var r = _bounds[index];
            int view = ClientSize.Height;
            if (r.Top < _scrollY) SetScroll(r.Top - Theme.S(6));
            else if (r.Bottom > _scrollY + view) SetScroll(r.Bottom - view + Theme.S(6));
        }

        public void ScrollToBottom()
        {
            SetScroll(_contentHeight);
        }

        private int ContentWidth
        {
            get { return ClientSize.Width - (_vbar.Visible ? _vbar.Width : 0); }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Relayout();
        }

        private int Pad { get { return Theme.S(6); } }
        private int Inner { get { return Theme.S(8); } }
        private int Gap { get { return Theme.S(5); } }

        /// <summary>คำนวณตำแหน่ง/ความสูงของการ์ดทุกใบ</summary>
        public void Relayout()
        {
            if (_textFont == null) return;
            bool needBar = ComputeLayout(ClientSize.Width);
            if (needBar)
                needBar = ComputeLayout(ClientSize.Width - _vbar.Width);
            _vbar.Visible = needBar;
            if (needBar)
            {
                _vbar.Minimum = 0;
                _vbar.Maximum = Math.Max(0, _contentHeight);
                _vbar.LargeChange = Math.Max(1, ClientSize.Height);
                _vbar.SmallChange = Theme.S(30);
            }
            SetScroll(_scrollY);
            Invalidate();
        }

        private bool ComputeLayout(int width)
        {
            _bounds.Clear();
            int y = Pad;
            int cardW = Math.Max(40, width - Pad * 2);
            int textW = Math.Max(20, cardW - Inner * 2);
            int lineH = TextRenderer.MeasureText("กขฟ้ปั่Ag", _textFont).Height;
            foreach (var s in _items)
            {
                int h = Inner;
                if (s.DisplayTitle != null)
                {
                    h += TextRenderer.MeasureText(s.DisplayTitle, _titleFont).Height;
                    h += Math.Min(lineH * 2, MeasureText(s.Text, _smallFont, textW));
                }
                else
                {
                    h += Math.Min(lineH * MaxPreviewLines, MeasureText(s.Text, _textFont, textW));
                }
                h += Inner;
                _bounds.Add(new Rectangle(Pad, y, cardW, h));
                y += h + Gap;
            }
            _contentHeight = y + Pad;
            return _contentHeight > ClientSize.Height;
        }

        private static int MeasureText(string text, Font font, int width)
        {
            if (string.IsNullOrEmpty(text)) text = " ";
            var sz = TextRenderer.MeasureText(text, font, new Size(width, int.MaxValue), TextFlags);
            return sz.Height;
        }

        private const TextFormatFlags TextFlags =
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        private void SetScroll(int y)
        {
            int max = Math.Max(0, _contentHeight - ClientSize.Height);
            y = Math.Max(0, Math.Min(max, y));
            _scrollY = y;
            if (_vbar.Visible && _vbar.Value != y)
                _vbar.Value = Math.Min(y, Math.Max(0, _vbar.Maximum - _vbar.LargeChange + 1));
            Invalidate();
        }

        public void ScrollBy(int delta)
        {
            SetScroll(_scrollY + delta);
        }

        private int HitTest(Point p)
        {
            int y = p.Y + _scrollY;
            if (p.X >= ContentWidth) return -1;
            for (int i = 0; i < _bounds.Count; i++)
                if (_bounds[i].Contains(p.X, y)) return i;
            return -1;
        }

        private int DropIndexAt(Point p)
        {
            int y = p.Y + _scrollY;
            for (int i = 0; i < _bounds.Count; i++)
                if (y < _bounds[i].Top + _bounds[i].Height / 2) return i;
            return _bounds.Count;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ScrollBy(-e.Delta * Theme.S(40) / 120);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _downIndex = HitTest(e.Location);
                _downPoint = e.Location;
                _dragging = false;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_downIndex >= 0 && e.Button == MouseButtons.Left && AllowReorder)
            {
                if (!_dragging && (Math.Abs(e.Y - _downPoint.Y) > SystemInformation.DragSize.Height * 2 ||
                                   Math.Abs(e.X - _downPoint.X) > SystemInformation.DragSize.Width * 2))
                {
                    _dragging = true;
                    Cursor = Cursors.SizeNS;
                    _tip.Hide(this);
                }
                if (_dragging)
                {
                    // เลื่อนอัตโนมัติเมื่อลากไปชิดขอบบน/ล่าง
                    int edge = Theme.S(24);
                    if (e.Y < edge) ScrollBy(-Theme.S(12));
                    else if (e.Y > ClientSize.Height - edge) ScrollBy(Theme.S(12));
                    int di = DropIndexAt(e.Location);
                    if (di != _dropIndex) { _dropIndex = di; Invalidate(); }
                    return;
                }
            }

            int h = HitTest(e.Location);
            if (h != _hover)
            {
                _hover = h;
                Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
                UpdateTip(h);
            }
        }

        private void UpdateTip(int index)
        {
            if (index == _tipIndex) return;
            _tipIndex = index;
            if (index < 0 || index >= _items.Count) { _tip.SetToolTip(this, null); return; }
            var s = _items[index];
            string tip = s.Text ?? "";
            if (Templates.HasVariables(tip))
                tip = tip + "\r\n\r\n→ " + Templates.Expand(tip);
            if (tip.Length > 600) tip = tip.Substring(0, 600) + "…";
            _tip.SetToolTip(this, tip);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!_dragging && _hover != -1) { _hover = -1; Invalidate(); }
            _tipIndex = -1;
            _tip.SetToolTip(this, null);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left)
            {
                if (_dragging)
                {
                    int from = _downIndex, to = _dropIndex;
                    _dragging = false;
                    _dropIndex = -1;
                    _downIndex = -1;
                    Cursor = Cursors.Default;
                    Invalidate();
                    if (to > from) to--; // ปรับเพราะรายการต้นทางถูกนำออกก่อน
                    if (from >= 0 && to >= 0 && to != from && ItemMoved != null)
                        ItemMoved(this, new ItemMovedEventArgs(from, to));
                    return;
                }
                int idx = HitTest(e.Location);
                if (idx >= 0 && idx == _downIndex && ItemClicked != null)
                    ItemClicked(this, new ItemEventArgs(idx, e.Location));
                _downIndex = -1;
            }
            else if (e.Button == MouseButtons.Right)
            {
                int idx = HitTest(e.Location);
                if (ItemRightClicked != null)
                    ItemRightClicked(this, new ItemEventArgs(idx, e.Location));
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (_items.Count == 0)
            {
                var r = new Rectangle(Theme.S(12), Theme.S(20), ClientSize.Width - Theme.S(24), ClientSize.Height - Theme.S(40));
                TextRenderer.DrawText(g, EmptyText, _textFont, r, Theme.SubText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                return;
            }

            int radius = Theme.S(6);
            for (int i = 0; i < _items.Count; i++)
            {
                var b = _bounds[i];
                b.Offset(0, -_scrollY);
                if (b.Bottom < 0 || b.Top > ClientSize.Height) continue;

                bool hover = i == _hover && !_dragging;
                bool flash = i == _flash;
                bool dragged = _dragging && i == _downIndex;
                Color fill = flash ? Theme.Success : hover ? Theme.CardHover : Theme.Card;
                Color border = flash ? Theme.SuccessBorder : hover ? Theme.CardHoverBorder : Theme.CardBorder;

                using (var path = Theme.RoundRect(b, radius))
                {
                    using (var br = new SolidBrush(dragged ? Color.FromArgb(235, 238, 242) : fill)) g.FillPath(br, path);
                    using (var pen = new Pen(border)) g.DrawPath(pen, path);
                }
                if (hover || flash)
                {
                    using (var br = new SolidBrush(flash ? Theme.SuccessBorder : Theme.Accent))
                        g.FillRectangle(br, b.X + 1, b.Y + radius, Theme.S(3), b.Height - radius * 2);
                }

                var s = _items[i];
                var tr = new Rectangle(b.X + Inner, b.Y + Inner, b.Width - Inner * 2, b.Height - Inner * 2);
                Color textColor = Templates.HasVariables(s.Text) ? Theme.VarText : Theme.Text;
                if (s.DisplayTitle != null)
                {
                    int th = TextRenderer.MeasureText(s.DisplayTitle, _titleFont).Height;
                    TextRenderer.DrawText(g, s.DisplayTitle, _titleFont, new Rectangle(tr.X, tr.Y, tr.Width, th), Theme.Accent,
                        TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                    var sub = new Rectangle(tr.X, tr.Y + th, tr.Width, tr.Height - th);
                    TextRenderer.DrawText(g, s.Text, _smallFont, sub, Theme.SubText, TextFlags | TextFormatFlags.EndEllipsis);
                }
                else
                {
                    TextRenderer.DrawText(g, s.Text, _textFont, tr, textColor, TextFlags | TextFormatFlags.EndEllipsis);
                }

                if (flash && !string.IsNullOrEmpty(_flashText))
                {
                    var badgeSize = TextRenderer.MeasureText(_flashText, _smallFont);
                    var badge = new Rectangle(b.Right - badgeSize.Width - Theme.S(10), b.Y + Theme.S(3), badgeSize.Width + Theme.S(6), badgeSize.Height);
                    using (var p = Theme.RoundRect(badge, Theme.S(4)))
                    using (var br = new SolidBrush(Theme.SuccessBorder))
                        g.FillPath(br, p);
                    TextRenderer.DrawText(g, _flashText, _smallFont, badge, Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                }
            }

            if (_dragging && _dropIndex >= 0)
            {
                int y;
                if (_dropIndex < _bounds.Count) y = _bounds[_dropIndex].Top - Gap / 2;
                else y = _bounds[_bounds.Count - 1].Bottom + Gap / 2;
                y -= _scrollY;
                using (var pen = new Pen(Theme.Accent, Theme.S(3)))
                    g.DrawLine(pen, Pad, y, ContentWidth - Pad, y);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _flashTimer.Dispose();
                _tip.Dispose();
                if (_titleFont != null) { _titleFont.Dispose(); _textFont.Dispose(); _smallFont.Dispose(); }
            }
            base.Dispose(disposing);
        }
    }
}
