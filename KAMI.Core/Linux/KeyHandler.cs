#if Linux
using System;
using System.Runtime.InteropServices;
using System.Threading;
using KAMI.Core.Common;

namespace KAMI.Core
{
    public class KeyHandler : IKeyHandler
    {
        [DllImport("libX11.so.6")] static extern int XInitThreads();
        [DllImport("libX11.so.6")] static extern IntPtr XOpenDisplay(IntPtr display);
        [DllImport("libX11.so.6")] static extern int XCloseDisplay(IntPtr dpy);
        [DllImport("libX11.so.6")] static extern ulong XDefaultRootWindow(IntPtr dpy);
        [DllImport("libX11.so.6")] static extern int XGrabKey(IntPtr dpy, int keycode, uint modifiers,
            ulong grab_window, bool owner_events, int pointer_mode, int keyboard_mode);
        [DllImport("libX11.so.6")] static extern int XUngrabKey(IntPtr dpy, int keycode, uint modifiers,
            ulong grab_window);
        [DllImport("libX11.so.6")] static extern int XGrabButton(IntPtr dpy, uint button, uint modifiers,
            ulong grab_window, bool owner_events, uint event_mask,
            int pointer_mode, int keyboard_mode, ulong confine_to, ulong cursor);
        [DllImport("libX11.so.6")] static extern int XUngrabButton(IntPtr dpy, uint button, uint modifiers,
            ulong grab_window);
        [DllImport("libX11.so.6")] static extern uint XKeysymToKeycode(IntPtr dpy, ulong keysym);
        [DllImport("libX11.so.6")] static extern int XPending(IntPtr dpy);
        [DllImport("libX11.so.6")] static extern int XNextEvent(IntPtr dpy, out XEvent ev);
        [DllImport("libX11.so.6")] static extern int XAllowEvents(IntPtr dpy, int event_mode, ulong time);
        [DllImport("libX11.so.6")] static extern int XFlush(IntPtr dpy);
        [DllImport("libXtst.so.6")] static extern int XTestFakeKeyEvent(IntPtr dpy,
            uint keycode, bool is_press, ulong delay);

        const int GrabModeSync       = 0;
        const int GrabModeAsync      = 1;
        const int AsyncPointer       = 1;
        const uint AnyModifier       = 1u << 15;
        const ulong CurrentTime      = 0;
        const uint Button1           = 1;
        const uint Button3           = 3;
        const uint ButtonPressMask   = 1u << 2;
        const uint ButtonReleaseMask = 1u << 3;
        const int KeyPressEvent      = 2;
        const int ButtonPressEvent   = 4;
        const int ButtonReleaseEvent = 5;

        public event KeyPressHandler OnKeyPress;

        readonly IntPtr _dpy;
        readonly ulong _root;
        readonly object _lock = new object();
        readonly Thread _eventThread;
        volatile bool _stopping;
        volatile uint _toggleKeycode;
        volatile int _mouse1Key = -1;
        volatile int _mouse2Key = -1;

        public KeyHandler()
        {
            XInitThreads();
            _dpy = XOpenDisplay(IntPtr.Zero);
            if (_dpy == IntPtr.Zero)
                throw new Exception("Failed to open X display");
            _root = XDefaultRootWindow(_dpy);
            _eventThread = new Thread(EventLoop)
            {
                IsBackground = true,
                Name = "KAMI X11 events"
            };
            _eventThread.Start();
        }

        public void SetHotKey(KeyType keyType, int? key)
        {
            switch (keyType)
            {
                case KeyType.InjectionToggle:
                    lock (_lock)
                    {
                        if (_toggleKeycode != 0)
                        {
                            XUngrabKey(_dpy, (int)_toggleKeycode, AnyModifier, _root);
                            _toggleKeycode = 0;
                        }
                        if (key.HasValue)
                        {
                            uint kc = XKeysymToKeycode(_dpy, (ulong)key.Value);
                            if (kc != 0)
                            {
                                XGrabKey(_dpy, (int)kc, AnyModifier, _root,
                                    false, GrabModeAsync, GrabModeAsync);
                                _toggleKeycode = kc;
                            }
                        }
                        XFlush(_dpy);
                    }
                    break;
                case KeyType.Mouse1:
                    _mouse1Key = key ?? -1;
                    break;
                case KeyType.Mouse2:
                    _mouse2Key = key ?? -1;
                    break;
            }
        }

        public void SetEnableMouseHook(bool enabled)
        {
            lock (_lock)
            {
                if (enabled)
                {
                    XGrabButton(_dpy, Button1, AnyModifier, _root, false,
                        ButtonPressMask | ButtonReleaseMask,
                        GrabModeSync, GrabModeAsync, 0, 0);
                    XGrabButton(_dpy, Button3, AnyModifier, _root, false,
                        ButtonPressMask | ButtonReleaseMask,
                        GrabModeSync, GrabModeAsync, 0, 0);
                }
                else
                {
                    XUngrabButton(_dpy, Button1, AnyModifier, _root);
                    XUngrabButton(_dpy, Button3, AnyModifier, _root);
                }
                XFlush(_dpy);
            }
        }

        private void EventLoop()
        {
            while (!_stopping)
            {
                int pending;
                lock (_lock) { pending = XPending(_dpy); }
                if (pending == 0) { Thread.Sleep(1); continue; }

                XEvent ev;
                lock (_lock) { XNextEvent(_dpy, out ev); }

                switch (ev.type)
                {
                    case KeyPressEvent:
                        if (_toggleKeycode != 0 && ev.detail == _toggleKeycode)
                            OnKeyPress?.Invoke(this);
                        break;

                    case ButtonPressEvent:
                    {
                        int key = ev.detail == Button1 ? _mouse1Key : _mouse2Key;
                        if (key >= 0)
                        {
                            lock (_lock)
                            {
                                uint kc = XKeysymToKeycode(_dpy, (ulong)key);
                                XTestFakeKeyEvent(_dpy, kc, true, 0);
                                XFlush(_dpy);
                            }
                        }
                        lock (_lock) { XAllowEvents(_dpy, AsyncPointer, CurrentTime); XFlush(_dpy); }
                        break;
                    }

                    case ButtonReleaseEvent:
                    {
                        int key = ev.detail == Button1 ? _mouse1Key : _mouse2Key;
                        if (key >= 0)
                        {
                            lock (_lock)
                            {
                                uint kc = XKeysymToKeycode(_dpy, (ulong)key);
                                XTestFakeKeyEvent(_dpy, kc, false, 0);
                                XFlush(_dpy);
                            }
                        }
                        lock (_lock) { XAllowEvents(_dpy, AsyncPointer, CurrentTime); XFlush(_dpy); }
                        break;
                    }
                }
            }
        }

        public void Dispose()
        {
            _stopping = true;
            _eventThread?.Join(500);
            lock (_lock)
            {
                if (_toggleKeycode != 0)
                    XUngrabKey(_dpy, (int)_toggleKeycode, AnyModifier, _root);
                XUngrabButton(_dpy, Button1, AnyModifier, _root);
                XUngrabButton(_dpy, Button3, AnyModifier, _root);
                XFlush(_dpy);
            }
            XCloseDisplay(_dpy);
        }
    }

    // Minimal union covering XKeyEvent and XButtonEvent on 64-bit Linux.
    // `detail` is `keycode` for key events and `button` for button events -
    // both are unsigned int at offset 84 in the shared event struct ABI.
    [StructLayout(LayoutKind.Explicit, Size = 192)]
    internal struct XEvent
    {
        [FieldOffset(0)]  public int type;
        [FieldOffset(84)] public uint detail;
    }
}
#endif
