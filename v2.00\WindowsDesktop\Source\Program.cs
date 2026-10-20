using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace WindowsKeyboardTyper
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool created;
            using(var mutex=new Mutex(true,"Local\\WindowsKeyboardTyper",out created))
            {
                if(!created) { MessageBox.Show("系统键盘输入工具已经打开，请切换到已有窗口。","逐字输入"); return; }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TyperForm());
            }
        }
    }
    public sealed class TyperForm : Form
    {
        readonly TextBox body=new TextBox();
        readonly NumericUpDown interval=new NumericUpDown(), delay=new NumericUpDown();
        readonly ComboBox mode=new ComboBox(), breaks=new ComboBox();
        readonly Button start=new Button(), stop=new Button();
        readonly Label status=new Label(), count=new Label();
        readonly ProgressBar progress=new ProgressBar();
        readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
        readonly Stopwatch clock=new Stopwatch();
        List<string> units;
        UserStopHooks hooks;
        IntPtr target, targetFocus;
        int position, gap, countdown;
        bool active, typing, physical, normalizing;
        LineBreakMode breakMode;
        long next;
        string cancelReason;

        public TyperForm()
        {
            Text="逐字输入 · Windows 系统键盘";
            Name="WindowsKeyboardTyper";
            StartPosition=FormStartPosition.CenterScreen;
            ClientSize=new Size(690,640); MinimumSize=new Size(620,640);
            Font=new Font("Microsoft YaHei UI",9F);
            BackColor=Color.FromArgb(245,247,252);
            var layout=new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=1, RowCount=8, Padding=new Padding(22) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,66));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,27));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,80));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,54));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,46));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,15));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,61));
            var heading=new Label { Dock=DockStyle.Fill,Text="Windows 系统键盘输入\n填写正文，倒计时内点击目标输入框；F8 / Esc 停止。",Font=new Font(Font.FontFamily,11F),ForeColor=Color.FromArgb(35,49,74) };
            count.Text="正文 · 仅保存在内存，不读取剪贴板";count.Dock=DockStyle.Fill;
            body.Multiline=true;body.AcceptsReturn=true;body.AcceptsTab=true;body.ScrollBars=ScrollBars.Vertical;body.MaxLength=100000;body.Dock=DockStyle.Fill;body.Name="Body";body.AccessibleName="需要输入的正文";
            body.TextChanged+=delegate {
                if(!normalizing)
                {
                    string original=body.Text, normalized=NativeKeyboard.NormalizeEditorText(original);
                    if(original!=normalized)
                    {
                        int caret=body.SelectionStart, selected=body.SelectionLength;
                        int newCaret=NativeKeyboard.NormalizeEditorText(original.Substring(0,caret)).Length;
                        int newSelected=NativeKeyboard.NormalizeEditorText(original.Substring(caret,selected)).Length;
                        normalizing=true;
                        try{body.Text=normalized;body.Select(Math.Min(newCaret,body.TextLength),Math.Min(newSelected,body.TextLength-Math.Min(newCaret,body.TextLength)));}
                        finally{normalizing=false;}
                    }
                }
                count.Text="正文 · "+body.TextLength.ToString("N0")+" 个 UTF-16 字符 · 本地运行";
            };
            var settings=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=4,RowCount=2,Padding=new Padding(0,9,0,0)};
            for(int i=0;i<4;i++) settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25));
            settings.RowStyles.Add(new RowStyle(SizeType.Absolute,24));settings.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            string[] names={"每字间隔（毫秒）","倒计时（秒）","输入模式","正文换行"};
            for(int i=0;i<4;i++) settings.Controls.Add(new Label {Text=names[i],Dock=DockStyle.Fill},i,0);
            interval.Minimum=25;interval.Maximum=2000;interval.Value=60;interval.Increment=5;interval.Dock=DockStyle.Top;interval.Name="Interval";interval.AccessibleName=names[0];
            delay.Minimum=2;delay.Maximum=30;delay.Value=5;delay.Dock=DockStyle.Top;delay.Name="Delay";delay.AccessibleName=names[1];
            mode.DropDownStyle=ComboBoxStyle.DropDownList;mode.Items.AddRange(new object[]{"Unicode 键盘","混合按键"});mode.SelectedIndex=0;mode.Dock=DockStyle.Top;mode.AccessibleName=names[2];mode.Name="InputMode";
            breaks.DropDownStyle=ComboBoxStyle.DropDownList;breaks.Items.AddRange(new object[]{"遇到换行停止","Shift + Enter","Enter"});breaks.SelectedIndex=1;breaks.Dock=DockStyle.Top;breaks.AccessibleName=names[3];breaks.Name="LineBreak";
            settings.Controls.Add(interval,0,1);settings.Controls.Add(delay,1,1);settings.Controls.Add(mode,2,1);settings.Controls.Add(breaks,3,1);
            var help=new Label {Dock=DockStyle.Fill,Text="Unicode 模式支持中文和 Emoji；混合模式的英文需使用英文输入法。\n默认用 Shift+Enter 换行；网站可能将按键解释为发送，请先短文试填。",ForeColor=Color.FromArgb(100,112,132),Padding=new Padding(0,8,0,0)};
            status.Dock=DockStyle.Fill;status.Text="准备就绪";status.Name="Status";status.AccessibleName="输入状态";status.BackColor=Color.FromArgb(232,238,251);status.Padding=new Padding(10);status.AutoEllipsis=true;
            progress.Dock=DockStyle.Fill;progress.Name="Progress";
            var actions=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,Padding=new Padding(0,12,0,0)};
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,75));actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25));
            start.Text="开始倒计时，然后点击目标输入框";start.Name="Start";start.Dock=DockStyle.Fill;start.FlatStyle=FlatStyle.Flat;start.BackColor=Color.FromArgb(81,99,217);start.ForeColor=Color.White;
            stop.Text="停止 · F8";stop.Name="Stop";stop.Dock=DockStyle.Fill;stop.Enabled=false;
            start.Click+=delegate {Arm();};stop.Click+=delegate {Finish("用户停止，已输入部分保留。");};
            actions.Controls.Add(start,0,0);actions.Controls.Add(stop,1,0);
            layout.Controls.Add(heading,0,0);layout.Controls.Add(count,0,1);layout.Controls.Add(body,0,2);layout.Controls.Add(settings,0,3);layout.Controls.Add(help,0,4);layout.Controls.Add(status,0,5);layout.Controls.Add(progress,0,6);layout.Controls.Add(actions,0,7);Controls.Add(layout);
            timer.Interval=15;timer.Tick+=delegate {Tick();};
            FormClosing+=delegate {Finish(null);};
        }
        void Arm()
        {
            if(active) return;
            if(body.TextLength==0) {status.Text="请先填写正文。";return;}
            if(body.TextLength>100000) {status.Text="正文最多 100,000 个 UTF-16 字符（含换行）。";return;}
            units=NativeKeyboard.Split(body.Text);position=0;gap=(int)interval.Value;countdown=(int)delay.Value;
            physical=mode.SelectedIndex==1;breakMode=(LineBreakMode)breaks.SelectedIndex;cancelReason=null;target=IntPtr.Zero;typing=false;
            progress.Value=0;progress.Maximum=Math.Max(1,units.Count);
            try { hooks=new UserStopHooks(delegate{return typing;},delegate(string reason){if(cancelReason==null)cancelReason=reason;}); }
            catch(Exception error) {units.Clear();units=null;status.Text="无法启用停止保护："+error.Message;return;}
            active=true;SetInputs(false);stop.Enabled=true;clock.Restart();timer.Start();
            status.Text=countdown+" 秒内点击网页输入框 · F8 / Esc 取消";
        }
        void Tick()
        {
            if(!active) return;
            if(cancelReason!=null) {Finish(cancelReason);return;}
            try
            {
                if(!typing)
                {
                    int left=countdown-(int)(clock.ElapsedMilliseconds/1000);
                    if(left>0) {status.Text=left+" 秒内点击网页输入框 · F8 / Esc 取消";return;}
                    target=NativeKeyboard.GetForegroundWindow();
                    uint process;NativeKeyboard.GetWindowThreadProcessId(target,out process);
                    if(target==IntPtr.Zero || process==(uint)Process.GetCurrentProcess().Id) {Finish("没有选中其他程序的输入框，请重新开始。");return;}
                    if(NativeKeyboard.ModifiersDown()) {Finish("请先松开 Shift、Ctrl、Alt、Win，再重新开始。");return;}
                    targetFocus=NativeKeyboard.FocusOf(target);
                    if(targetFocus==IntPtr.Zero) {Finish("目标没有可用焦点，请点击输入框后重新开始。");return;}
                    typing=true;next=clock.ElapsedMilliseconds;
                }
                if(!NativeKeyboard.TargetActive(target,targetFocus)) {Finish("目标窗口或控件焦点改变，输入已停止。");return;}
                if(clock.ElapsedMilliseconds<next) return;
                if(position>=units.Count) {Finish("输入完成，共 "+position+" 个文本单元。请检查内容后手动提交。");return;}
                NativeKeyboard.Send(units[position],physical,breakMode,target,targetFocus);
                position++;progress.Value=position;
                status.Text="正在输入 "+position+" / "+units.Count+" · F8 / Esc 停止";
                next=clock.ElapsedMilliseconds+gap;
            }
            catch(Exception error) {Finish("已停止（"+position+"）："+error.Message);}
        }
        void SetInputs(bool enabled)
        {
            body.ReadOnly=!enabled;interval.Enabled=enabled;delay.Enabled=enabled;mode.Enabled=enabled;breaks.Enabled=enabled;start.Enabled=enabled;
        }
        void Finish(string message)
        {
            timer.Stop();clock.Stop();typing=false;active=false;
            if(hooks!=null) {hooks.Dispose();hooks=null;}
            if(units!=null) {units.Clear();units=null;}
            stop.Enabled=false;SetInputs(true);
            if(message!=null) status.Text=message;
        }
    }
    sealed class UserStopHooks : IDisposable
    {
        delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        readonly HookProc keyboardProc,mouseProc;
        readonly Func<bool> running;
        readonly Action<string> cancel;
        IntPtr keyboard,mouse;
        [StructLayout(LayoutKind.Sequential)] struct KeyEvent {public uint Key,Scan,Flags,Time;public UIntPtr Extra;}
        [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetWindowsHookEx(int id,HookProc proc,IntPtr module,uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandle(string module);
        public UserStopHooks(Func<bool> isRunning,Action<string> onCancel)
        {
            running=isRunning;cancel=onCancel;keyboardProc=Keyboard;mouseProc=Mouse;
            keyboard=SetWindowsHookEx(13,keyboardProc,GetModuleHandle(null),0);
            if(keyboard==IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            mouse=SetWindowsHookEx(14,mouseProc,GetModuleHandle(null),0);
            if(mouse==IntPtr.Zero) {int error=Marshal.GetLastWin32Error();Dispose();throw new Win32Exception(error);}
        }
        IntPtr Keyboard(int code,IntPtr message,IntPtr data)
        {
            if(code>=0 && (message.ToInt32()==0x100 || message.ToInt32()==0x104))
            {
                KeyEvent key=(KeyEvent)Marshal.PtrToStructure(data,typeof(KeyEvent));
                if(key.Extra!=NativeKeyboard.Tag)
                {
                    if(key.Key==0x77 || key.Key==0x1B) {cancel("快捷键停止，已输入部分保留。");return new IntPtr(1);}
                    if(running()) cancel("检测到其他键盘输入，自动输入已停止。");
                }
            }
            return CallNextHookEx(keyboard,code,message,data);
        }
        IntPtr Mouse(int code,IntPtr message,IntPtr data)
        {
            int kind=message.ToInt32();
            if(code>=0 && running() && (kind==0x201 || kind==0x204 || kind==0x207 || kind==0x20A || kind==0x20B)) cancel("检测到点击或滚动，自动输入已停止。");
            return CallNextHookEx(mouse,code,message,data);
        }
        public void Dispose()
        {
            if(keyboard!=IntPtr.Zero){UnhookWindowsHookEx(keyboard);keyboard=IntPtr.Zero;}
            if(mouse!=IntPtr.Zero){UnhookWindowsHookEx(mouse);mouse=IntPtr.Zero;}
        }
    }
}
