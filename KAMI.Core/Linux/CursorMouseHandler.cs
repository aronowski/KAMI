#if Linux
using System;
using System.Runtime.InteropServices;
using KAMI.Core.Common;

namespace KAMI.Core
{
    internal class CursorMouseHandler : IMouseHandler, IDisposable
    {
        [DllImport("libX11.so.6")] static extern IntPtr XOpenDisplay(IntPtr display);
        [DllImport("libX11.so.6")] static extern int XCloseDisplay(IntPtr dpy);
        [DllImport("libX11.so.6")] static extern int XGetInputFocus(IntPtr dpy,
            out ulong window, out int revert_to);
        [DllImport("libX11.so.6")] static extern int XGetGeometry(IntPtr dpy, ulong drawable,
            out ulong root, out int x, out int y,
            out uint width, out uint height,
            out uint border_width, out uint depth);
        [DllImport("libX11.so.6")] static extern bool XQueryPointer(IntPtr dpy, ulong window,
            out ulong root_return, out ulong child_return,
            out int root_x, out int root_y,
            out int win_x, out int win_y,
            out uint mask_return);
        [DllImport("libX11.so.6")] static extern int XWarpPointer(IntPtr dpy,
            ulong src_w, ulong dst_w,
            int src_x, int src_y, uint src_width, uint src_height,
            int dest_x, int dest_y);
        [DllImport("libX11.so.6")] static extern int XGrabPointer(IntPtr dpy, ulong grab_window,
            bool owner_events, uint event_mask,
            int pointer_mode, int keyboard_mode,
            ulong confine_to, ulong cursor, ulong time);
        [DllImport("libX11.so.6")] static extern int XUngrabPointer(IntPtr dpy, ulong time);
        [DllImport("libX11.so.6")] static extern int XFlush(IntPtr dpy);

        const ulong CurrentTime = 0;
        const int GrabModeAsync = 1;

        readonly IntPtr _dpy;

        internal CursorMouseHandler()
        {
            _dpy = XOpenDisplay(IntPtr.Zero);
            if (_dpy == IntPtr.Zero)
                throw new Exception("Failed to open X display");
        }

        public (int, int) GetCenterDiff()
        {
            XGetInputFocus(_dpy, out ulong window, out _);
            if (window <= 1)
                return (0, 0);

            XGetGeometry(_dpy, window,
                out _, out _, out _,
                out uint w, out uint h,
                out _, out _);

            int cx = (int)(w / 2);
            int cy = (int)(h / 2);

            XQueryPointer(_dpy, window,
                out _, out _,
                out _, out _,
                out int winX, out int winY,
                out _);

            XWarpPointer(_dpy, 0, window, 0, 0, 0, 0, cx, cy);
            XFlush(_dpy);

            return (winX - cx, winY - cy);
        }

        public void ConfineCursor()
        {
            XGetInputFocus(_dpy, out ulong window, out _);
            if (window <= 1)
                return;

            XGrabPointer(_dpy, window, false, 0,
                GrabModeAsync, GrabModeAsync,
                window, 0, CurrentTime);
            XFlush(_dpy);
        }

        public void ReleaseCursor()
        {
            XUngrabPointer(_dpy, CurrentTime);
            XFlush(_dpy);
        }

        public void Dispose()
        {
            if (_dpy != IntPtr.Zero)
                XCloseDisplay(_dpy);
        }
    }
}
#endif
