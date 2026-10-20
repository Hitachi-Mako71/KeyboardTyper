using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;

namespace WindowsKeyboardTyper
{
    public enum LineBreakMode { Pause, ShiftEnter, Enter }

    [StructLayout(LayoutKind.Sequential)]
    public struct KeyboardData
    {
        public ushort VirtualKey, Scan;
        public uint Flags, Time;
        public UIntPtr Extra;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct MouseData
    {
        public int X, Y;
        public uint Data, Flags, Time;
        public UIntPtr Extra;
    }
    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public KeyboardData Keyboard;
        [FieldOffset(0)] public MouseData Mouse;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct NativeInput
    {
        public uint Type;
        public InputUnion Data;
    }
    public static class NativeKeyboard
    {
        public static readonly UIntPtr Tag = new UIntPtr(0x574B5459);
        public const uint KeyUp = 2, Unicode = 4, ScanCode = 8;
        [DllImport("user32.dll", SetLastError=true)] private static extern uint SendInput(uint count, NativeInput[] inputs, int size);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern short VkKeyScanEx(char value, IntPtr layout);
        [DllImport("user32.dll")] private static extern uint MapVirtualKeyEx(uint key, uint map, IntPtr layout);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint thread);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr handle, out uint process);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] private static extern short GetKeyState(int key);

        public static NativeInput[] Build(string unit, bool physicalAscii, LineBreakMode breaks, IntPtr layout, bool capsLock)
        {
            if (String.IsNullOrEmpty(unit)) throw new ArgumentException("Empty input unit");
            var result = new List<NativeInput>();
            if (unit == "\n")
            {
                if (breaks == LineBreakMode.Pause) throw new InvalidOperationException("到达正文换行，已停止。请手动换行，并只填写尚未输入的正文后重新开始。");
                if (breaks == LineBreakMode.ShiftEnter) result.Add(Key(0x10,0,0));
                result.Add(Key(13,0,0)); result.Add(Key(13,0,KeyUp));
                if (breaks == LineBreakMode.ShiftEnter) result.Add(Key(0x10,0,KeyUp));
                return result.ToArray();
            }
            if (physicalAscii && unit.Length == 1 && unit[0] >= ' ' && unit[0] <= '~')
            {
                short mapping = VkKeyScanEx(unit[0],layout);
                int modifiers = (mapping >> 8) & 255;
                if (mapping != -1 && (modifiers & ~1) == 0)
                {
                    ushort vk = (ushort)(mapping & 255);
                    bool shift = (modifiers & 1) != 0;
                    if (vk >= 65 && vk <= 90 && capsLock) shift = !shift;
                    ushort scan = (ushort)MapVirtualKeyEx(vk,0,layout);
                    ushort shiftScan = (ushort)MapVirtualKeyEx(0xA0,0,layout);
                    if (scan != 0 && shiftScan != 0)
                    {
                        if (shift) result.Add(Key(0,shiftScan,ScanCode));
                        result.Add(Key(0,scan,ScanCode)); result.Add(Key(0,scan,ScanCode|KeyUp));
                        if (shift) result.Add(Key(0,shiftScan,ScanCode|KeyUp));
                        return result.ToArray();
                    }
                }
            }
            // Unicode packets also have down/up pairs; UTF-16 surrogates share one SendInput batch.
            foreach (char value in unit)
            {
                result.Add(Key(0,value,Unicode)); result.Add(Key(0,value,Unicode|KeyUp));
            }
            return result.ToArray();
        }
        private static NativeInput Key(ushort vk, ushort scan, uint flags)
        {
            return new NativeInput { Type=1, Data=new InputUnion { Keyboard=new KeyboardData { VirtualKey=vk, Scan=scan, Flags=flags, Extra=Tag } } };
        }
        public static List<string> Split(string text)
        {
            var result = new List<string>();
            var iterator = StringInfo.GetTextElementEnumerator(text.Replace("\r\n","\n").Replace("\r","\n"));
            while(iterator.MoveNext()) result.Add(iterator.GetTextElement());
            return result;
        }
        public static string NormalizeEditorText(string text)
        {
            return text.Replace("\r\n","\n").Replace("\r","\n").Replace("\n","\r\n");
        }
        public static bool ModifiersDown()
        {
            foreach(int key in new int[] {0x10,0x11,0x12,0x5B,0x5C}) if((GetAsyncKeyState(key)&0x8000)!=0) return true;
            return false;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct GuiInfo
        {
            public int Size, Flags;
            public IntPtr Active, Focus, Capture, Menu, Move, Caret;
            public int Left, Top, Right, Bottom;
        }
        [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiInfo info);
        public static IntPtr FocusOf(IntPtr window)
        {
            uint process;
            uint thread=GetWindowThreadProcessId(window,out process);
            var info=new GuiInfo { Size=Marshal.SizeOf(typeof(GuiInfo)) };
            return GetGUIThreadInfo(thread,ref info)?info.Focus:IntPtr.Zero;
        }
        public static bool TargetActive(IntPtr window, IntPtr focus)
        {
            return window!=IntPtr.Zero && GetForegroundWindow()==window && focus!=IntPtr.Zero && FocusOf(window)==focus;
        }
        public static void Send(string unit, bool physicalAscii, LineBreakMode breaks, IntPtr target, IntPtr focus)
        {
            if(!TargetActive(target,focus)) throw new InvalidOperationException("目标窗口或输入控件已失去焦点，输入停止。");
            if(ModifiersDown()) throw new InvalidOperationException("检测到 Shift、Ctrl、Alt 或 Win 按下，输入停止。");
            uint process;
            IntPtr layout=GetKeyboardLayout(GetWindowThreadProcessId(target,out process));
            NativeInput[] inputs=Build(unit,physicalAscii,breaks,layout,(GetKeyState(0x14)&1)!=0);
            if(!TargetActive(target,focus)) throw new InvalidOperationException("目标焦点已改变，输入停止。");
            uint sent=SendInput((uint)inputs.Length,inputs,Marshal.SizeOf(typeof(NativeInput)));
            if(sent!=(uint)inputs.Length)
            {
                int error=Marshal.GetLastWin32Error();
                var releases=new List<NativeInput>();
                foreach(NativeInput input in inputs) if((input.Data.Keyboard.Flags&KeyUp)!=0) releases.Add(input);
                if(releases.Count>0) SendInput((uint)releases.Count,releases.ToArray(),Marshal.SizeOf(typeof(NativeInput)));
                throw new Win32Exception(error,"Windows 未接受全部输入。请检查目标程序权限和输入框状态。");
            }
        }
    }
}
