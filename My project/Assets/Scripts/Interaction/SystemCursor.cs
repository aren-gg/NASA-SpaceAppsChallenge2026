using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace MarsSurvival.Interaction
{
    /// <summary>
    /// Operating-system pointer control that Unity lacks. <see cref="Pin"/> freezes the
    /// pointer where it is while the mouse keeps reporting movement, which is how a
    /// right-drag camera turn keeps the pointer visible at the spot you clicked (Unity's
    /// locked cursor would hide it and move it to the centre). macOS detaches the pointer
    /// from the mouse (CGAssociateMouseAndMouseCursorPosition); Windows clips it to a
    /// one-pixel box (ClipCursor). Elsewhere Pin returns false and callers fall back to
    /// Unity's lock. Positions are in the OS's global screen coordinates.
    /// </summary>
    public static class SystemCursor
    {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        [StructLayout(LayoutKind.Sequential)]
        private struct CGPoint { public double x, y; }

        [DllImport(CoreGraphics)] private static extern IntPtr CGEventCreate(IntPtr source);
        [DllImport(CoreGraphics)] private static extern CGPoint CGEventGetLocation(IntPtr eventRef);
        [DllImport(CoreGraphics)] private static extern int CGWarpMouseCursorPosition(CGPoint point);
        [DllImport(CoreGraphics)] private static extern int CGAssociateMouseAndMouseCursorPosition(int connected);
        [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr reference);

        public static bool TryGet(out Vector2 position)
        {
            position = Vector2.zero;
            try
            {
                IntPtr e = CGEventCreate(IntPtr.Zero);
                if (e == IntPtr.Zero) return false;
                CGPoint p = CGEventGetLocation(e);
                CFRelease(e);
                position = new Vector2((float)p.x, (float)p.y);
                return true;
            }
            catch (Exception) { return false; }
        }

        public static bool Pin(bool pinned)
        {
            try { return CGAssociateMouseAndMouseCursorPosition(pinned ? 0 : 1) == 0; }
            catch (Exception) { return false; }
        }

        public static void Set(Vector2 position)
        {
            try
            {
                CGWarpMouseCursorPosition(new CGPoint { x = position.x, y = position.y });
                // A warp briefly freezes the pointer; re-link it to the mouse straight away.
                CGAssociateMouseAndMouseCursorPosition(1);
            }
            catch (Exception) { }
        }
#elif UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x, y; }

        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }
        [DllImport("user32.dll")] private static extern bool ClipCursor(ref RECT rect);
        [DllImport("user32.dll", EntryPoint = "ClipCursor")] private static extern bool ClipCursorOff(IntPtr none);

        public static bool Pin(bool pinned)
        {
            try
            {
                if (!pinned) return ClipCursorOff(IntPtr.Zero);
                if (!GetCursorPos(out POINT p)) return false;
                RECT r = new RECT { left = p.x, top = p.y, right = p.x + 1, bottom = p.y + 1 };
                return ClipCursor(ref r);
            }
            catch (Exception) { return false; }
        }

        public static bool TryGet(out Vector2 position)
        {
            position = Vector2.zero;
            try
            {
                if (!GetCursorPos(out POINT p)) return false;
                position = new Vector2(p.x, p.y);
                return true;
            }
            catch (Exception) { return false; }
        }

        public static void Set(Vector2 position)
        {
            try { SetCursorPos(Mathf.RoundToInt(position.x), Mathf.RoundToInt(position.y)); }
            catch (Exception) { }
        }
#else
        public static bool TryGet(out Vector2 position) { position = Vector2.zero; return false; }
        public static bool Pin(bool pinned) { return false; }
        public static void Set(Vector2 position) { }
#endif
    }
}
