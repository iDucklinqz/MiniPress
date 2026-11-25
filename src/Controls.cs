using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MiniPress
{
    /// <summary>Layout is written in 96-DPI units and scaled here; fonts scale by themselves.</summary>
    static class Dpi
    {
        public static readonly float Factor = ReadFactor();

        static float ReadFactor()
        {
            try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX / 96f; }
            catch (Exception) { return 1f; }
        }

        public static int S(float v) { return (int)Math.Round(v * Factor); }
        public static Rectangle R(int x, int y, int w, int h) { return new Rectangle(S(x), S(y), S(w), S(h)); }
    }

    /// <summary>A label with a dashed border: the place to drop or click for files.</summary>
    sealed class DropZone : Label
    {
        bool dragActive;

        public DropZone()
        {
            AutoSize = false;
            TextAlign = ContentAlignment.MiddleCenter;
            UseMnemonic = false;
            AutoEllipsis = true;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public bool DragActive
        {
            get { return dragActive; }
            set { if (dragActive != value) { dragActive = value; Invalidate(); } }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(dragActive ? SystemColors.Highlight : SystemColors.ControlDark, Dpi.S(1)))
            {
                pen.DashStyle = dragActive ? DashStyle.Solid : DashStyle.Dash;
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }
    }
}
