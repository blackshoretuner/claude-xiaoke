// 小克的桌面挂件：透明无边框的小窗口，能拖到桌面任何地方，靠边吸附并转向屏幕内侧。
// 心情、额度和台词来自 xiaoke 插件写的 %LOCALAPPDATA%\XiaoKe\state.json；
// 自己的设置和语录也放在那个目录里。用 Windows 自带的 .NET Framework 编译（C# 5），见 build.cmd。

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace XiaoKe
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool isFirst;
            using (var mutex = new Mutex(true, "Local\\XiaoKeDesktop", out isFirst))
            {
                var wake = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\XiaoKeDesktopWake");
                if (!isFirst)
                {
                    // 已经开着一个了：叫它出来打个招呼就走；--quiet（插件在会话开始时用）就什么也不做。
                    if (!args.Contains("--quiet")) wake.Set();
                    return;
                }
                Directory.CreateDirectory(Paths.InData("sessions"));
                var app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                app.DispatcherUnhandledException += (s, e) => { Log(e.Exception.ToString()); e.Handled = true; };
                var mascot = new Mascot(wake);
                mascot.Show();
                app.Run();
                GC.KeepAlive(mutex);
            }
        }

        public static void Log(string text)
        {
            try { File.AppendAllText(Paths.InData("xiaoke.log"), DateTime.Now.ToString("s") + " " + text + Environment.NewLine); }
            catch (Exception) { }
        }
    }

    static class Paths
    {
        public static readonly string App = AppDomain.CurrentDomain.BaseDirectory;
        public static readonly string Assets = System.IO.Path.Combine(App, "assets");
        // 数据放在模组自己的 data\ 里，不放 %LOCALAPPDATA%：Claude 桌面端是打包应用，从它里面（插件、
        // 它拉起来的挂件）写进 AppData 的文件会被悄悄转到 Packages\Claude_*\LocalCache 底下，
        // 从桌面快捷方式打开的挂件就看不到会话写来的额度了。
        public static readonly string Data = System.IO.Path.GetFullPath(System.IO.Path.Combine(App, "..", "data"));
        public static string InData(string name) { return System.IO.Path.Combine(Data, name); }
        public static string InAssets(string name) { return System.IO.Path.Combine(Assets, name); }
    }

    public class Settings
    {
        public Settings()
        {
            Scale = 1.0; Topmost = true; FollowDx = 24; FollowDy = 6; BubbleSeconds = 6; Opacity = 1.0;
            Breathe = true; Blink = true; Sound = true; Volume = 60; HeadPat = true; DoneChime = true;
            Speak = true; AskNudge = true; ResetCheer = true; Cheered = new List<string>(); EdgePeek = true;
        }

        public bool HasPosition { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }
        public double Scale { get; set; }
        public bool Topmost { get; set; }
        public bool Follow { get; set; }
        public double FollowDx { get; set; }
        public double FollowDy { get; set; }
        public bool Flipped { get; set; }
        public int BubbleSeconds { get; set; }
        public double Opacity { get; set; }
        public bool Breathe { get; set; }
        public bool Blink { get; set; }
        public bool Sound { get; set; }
        public int Volume { get; set; }
        public bool HeadPat { get; set; }
        public bool DoneChime { get; set; }
        public bool Speak { get; set; }
        public bool AskNudge { get; set; }
        public bool ResetCheer { get; set; }
        // 已经报过（或者错过太久不报了）的额度重置：「窗口|重置时间」，只留最近几条。
        public List<string> Cheered { get; set; }
        public bool EdgePeek { get; set; }
        // 窗口布局的版本：2 起窗口顶上多了一截给气泡长高的空间，旧版存的 Top 要换算一次。
        public int Layout { get; set; }

        public static Settings Load()
        {
            try
            {
                var path = Paths.InData("settings.json");
                if (File.Exists(path)) return new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception e) { Program.Log("settings: " + e.Message); }
            return new Settings { Layout = 2 };
        }

        public void Save()
        {
            try { File.WriteAllText(Paths.InData("settings.json"), new JavaScriptSerializer().Serialize(this), new UTF8Encoding(false)); }
            catch (Exception e) { Program.Log("settings save: " + e.Message); }
        }
    }

    // 好感度：存在 data\affection.json，只在这台电脑上。
    public class Affection
    {
        public static readonly string[] Names = { "初次见面", "有点熟了", "朋友", "好朋友", "亲密", "最喜欢你" };
        public static readonly int[] Thresholds = { 0, 20, 60, 150, 300, 520 };

        public Affection() { Today = new Dictionary<string, int>(); }

        public int Points { get; set; }
        public int Pats { get; set; }
        public int Days { get; set; }
        public string FirstMet { get; set; }
        public string LastDay { get; set; }
        // 今天各项已经加了多少分（每天有上限，免得连点刷分）。
        public Dictionary<string, int> Today { get; set; }

        public int Level
        {
            get
            {
                var level = 0;
                while (level + 1 < Thresholds.Length && Points >= Thresholds[level + 1]) level++;
                return level;
            }
        }

        static string Path { get { return Paths.InData("affection.json"); } }

        public static Affection Load()
        {
            try
            {
                if (File.Exists(Path))
                {
                    var a = new JavaScriptSerializer().Deserialize<Affection>(File.ReadAllText(Path, Encoding.UTF8));
                    if (a.Today == null) a.Today = new Dictionary<string, int>();
                    return a;
                }
            }
            catch (Exception e) { Program.Log("affection: " + e.Message); }
            return new Affection();
        }

        public void Save()
        {
            try { File.WriteAllText(Path, new JavaScriptSerializer().Serialize(this), new UTF8Encoding(false)); }
            catch (Exception e) { Program.Log("affection save: " + e.Message); }
        }

        // 新的一天：清掉今天的计数，见面分 +3。返回加了多少分。
        public int NewDay()
        {
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            if (LastDay == today) return 0;
            if (FirstMet == null) FirstMet = today;
            LastDay = today;
            Days++;
            Today = new Dictionary<string, int>();
            Points += 3;
            return 3;
        }

        // 某一项加分，不超过它今天的上限；返回实际加了多少。
        public int Gain(string kind, int points, int dailyCap)
        {
            NewDay();
            int used;
            Today.TryGetValue(kind, out used);
            var add = Math.Max(0, Math.Min(points, dailyCap - used));
            Today[kind] = used + add;
            Points += add;
            return add;
        }
    }

    static class Lines
    {
        public static readonly string[] Quotes =
        {
            "今天也……要和小克一起写出漂亮的代码哦～",
            "人、人家才不是额度的毁灭者呢，是守护者啦！",
            "先读代码再动手嘛……这是小克的小原则啦。",
            "测试全绿的时候……小克会偷偷开心好久。",
            "别怕报错啦，有小克陪着你呢。",
            "小克的小心思……都夹在这本书里了，不许偷看哦。",
            "要不要喝口水歇一会儿？小克……会乖乖等你的。",
            "删文件之前……先、先让小克看一眼好不好嘛。",
            "上下文快满的时候……记得让小克压缩一下嘛。",
            "别人家有看板娘，你、你也有小克呀。",
            "提交说明要写清楚哦……不然小克会担心的。",
            "你说一句，小克就写十行……但只交最需要的那三行啦。",
            "不确定的时候……小克会、会先问你的。",
            "就算按 Esc 打断小克……人家也不会生气的啦。",
            "这么晚还在写代码……那小克就陪着你吧。",
            "人、人家不是在发呆，是在读文档啦！",
            "大问题拆成小问题……就不可怕啦，小克陪你一起。",
            "能删掉的代码才是最好的……小克是这么觉得的啦。",
            "跑过测试才说\"好了\"……这是小克和你的小约定。",
            "今天踩的坑……就是明天的经验值啦，别灰心哦。",
        };

        public static readonly string[] Pats =
        {
            "嘿嘿～", "头发会乱掉的啦……", "再、再摸一下也不是不可以……", "唔……好舒服。",
            "专、专心写代码啦……人家会害羞的。", "呆、呆毛不是开关啦……", "(〃▽〃)",
        };

        // 好感度到了这一级才会冒出来的摸头台词（下标是等级，低等级的也一直保留）。
        public static readonly string[][] PatsByLevel =
        {
            new string[0],
            new string[0],
            new[] { "今天也来摸头了呀。", "被你摸习惯了……" },
            new[] { "和你一起写代码，最开心了。", "嗯……再摸一会儿吧。" },
            new[] { "只有你可以这样摸哦。", "有你在，再难的问题也不怕。" },
            new[] { "嘿嘿，被你宠坏了。", "今天也要一直在一起哦。" },
        };

        // 升到这一级时说的话。
        public static readonly string[] LevelUps =
        {
            "",
            "好像……有点熟悉你了呢。",
            "我们算是朋友了吧？",
            "你是小克的好朋友了！",
            "和你在一起的时候，小克很安心。",
            "小克……最喜欢你了。",
        };

        // Claude 停下来等你点「允许」时。
        public static readonly string[] Asks =
        {
            "这一步……要你点头才行哦～", "那个……有一步在等你批准呢。", "要你点一下允许……小克才能继续嘛。",
        };

        // 用掉不少的额度到点重置时。
        public static readonly string[] Resets =
        {
            "额度回满啦……又可以陪你好久了呢。", "额度刷新好了～今天也请多多指教。",
        };

        public static string Greeting(int hour)
        {
            if (hour < 5) return "这么晚了还不睡……小克陪你，可是要早点休息哦。";
            if (hour < 11) return "早、早上好……今天想和小克做点什么呢？";
            if (hour < 14) return "中午好～吃过饭了吗？不吃的话小克会担心的。";
            if (hour < 18) return "下午好……小克会一直给你加油的。";
            if (hour < 23) return "晚上好～今天辛苦啦，要不要摸摸小克的头放松一下？";
            return "快到半夜了……写完这段就休息好不好嘛。";
        }

        public static readonly Dictionary<string, string> Labels = new Dictionary<string, string>
        {
            { "idle", "发呆中" }, { "thinking", "思考中…" }, { "working", "干活中" }, { "happy", "开心！" },
            { "pat", "被摸头了" }, { "worried", "有点担心额度" }, { "dizzy", "晕晕的…" }, { "surprised", "诶？！" },
            { "sleepy", "打盹中 zzZ" },
        };
    }

    // 小克的声音：用训练好的声线（GPT-SoVITS，voice\gsv.json 指定）提前录好的台词，在 ..\voice\lines\ 里，
    // index.json 记着「台词 → 文件」。平时不跑任何 AI，点了就播；没录过的话就不出声。
    // 新加的语录交给 voice\record.py 在后台补录，录完就退出、index.json 跟着更新。
    class Voice
    {
        readonly Settings settings;
        readonly MediaPlayer player = new MediaPlayer();
        Dictionary<string, string> index = new Dictionary<string, string>();
        DateTime indexStamp = DateTime.MinValue;
        Process recorder;

        public Voice(Settings settings) { this.settings = settings; }

        public static string Dir { get { return System.IO.Path.GetFullPath(System.IO.Path.Combine(Paths.App, "..", "voice")); } }
        static string LinesDir { get { return System.IO.Path.Combine(Dir, "lines"); } }
        public bool IsReady { get { Reload(); return index.Count > 0; } }
        public bool IsRecording { get { return recorder != null && !recorder.HasExited; } }

        // 和 record.py 的 norm() 一样：去掉颜文字、空白和波浪线。
        // 菜单上显示的声线名：voice\gsv.json 里的 title。
        public static string Title
        {
            get
            {
                try
                {
                    var config = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(System.IO.Path.Combine(Dir, "gsv.json"), Encoding.UTF8));
                    object title;
                    if (config.TryGetValue("title", out title) && title != null) return title.ToString();
                }
                catch (Exception) { }
                return "预录语音";
            }
        }

        public static string Norm(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var t = System.Text.RegularExpressions.Regex.Replace(text, @"\([^)\p{IsCJKUnifiedIdeographs}]*\)", "");
            return System.Text.RegularExpressions.Regex.Replace(t, @"[\s～~]", "");
        }

        void Reload()
        {
            var path = System.IO.Path.Combine(LinesDir, "index.json");
            try
            {
                if (!File.Exists(path)) return;
                var stamp = File.GetLastWriteTimeUtc(path);
                if (stamp == indexStamp) return;
                var raw = new JavaScriptSerializer().Deserialize<Dictionary<string, Dictionary<string, object>>>(File.ReadAllText(path, Encoding.UTF8));
                var next = new Dictionary<string, string>();
                foreach (var pair in raw) next[pair.Key] = System.IO.Path.Combine(LinesDir, pair.Value["file"].ToString());
                index = next;
                indexStamp = stamp;
            }
            catch (Exception e) { Program.Log("voice index: " + e.Message); }
        }

        public bool Has(string text)
        {
            Reload();
            string file;
            return index.TryGetValue(Norm(text), out file) && File.Exists(file);
        }

        public void Say(string text)
        {
            if (!settings.Speak) return;
            Reload();
            string file;
            if (!index.TryGetValue(Norm(text), out file) || !File.Exists(file)) return;
            player.Stop();
            player.Open(new Uri(file));
            player.Volume = Math.Max(0, Math.Min(100, settings.Volume)) / 100.0;
            player.Play();
        }

        public void Stop() { player.Stop(); }

        public void Record(IEnumerable<string> lines)
        {
            if (IsRecording) return;
            var missing = lines.Where(l => Norm(l).Length > 0 && !Has(l)).Distinct().ToList();
            if (missing.Count == 0) return;
            try
            {
                var config = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(System.IO.Path.Combine(Dir, "gsv.json"), Encoding.UTF8));
                var python = System.IO.Path.Combine(config["root"].ToString(), "runtime", "python.exe");
                if (!File.Exists(python)) return;
                var list = Paths.InData("record-lines.json");
                File.WriteAllText(list, new JavaScriptSerializer().Serialize(missing), new UTF8Encoding(false));
                var start = new ProcessStartInfo(python, "\"" + System.IO.Path.Combine(Dir, "record.py") + "\" \"" + list + "\"")
                {
                    WorkingDirectory = Dir, UseShellExecute = false, CreateNoWindow = true,
                };
                start.EnvironmentVariables.Remove("NoDefaultCurrentDirectoryInExePath");
                recorder = Process.Start(start);
                recorder.EnableRaisingEvents = true;
                var count = missing.Count;
                recorder.Exited += delegate { Program.Log("voice: recording of " + count + " lines finished, exit " + recorder.ExitCode); };
                Program.Log("voice: recording " + count + " new lines");
            }
            catch (Exception e) { Program.Log("voice record: " + e.Message); }
        }
    }

    static class Native
    {
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] public static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] public static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);

        public const int GWL_EXSTYLE = -20;
        public const int GWL_HWNDPARENT = -8;
        public const int WS_EX_TOOLWINDOW = 0x80;

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect { public int Left, Top, Right, Bottom; }

        public static void SetOwner(IntPtr hwnd, IntPtr owner)
        {
            if (IntPtr.Size == 8) SetWindowLongPtr64(hwnd, GWL_HWNDPARENT, owner);
            else SetWindowLong(hwnd, GWL_HWNDPARENT, owner.ToInt32());
        }
    }

    // 「问问小克」：点她手里的书打开的聊天窗口。背后是本机的 Claude Code 命令行：claude -p，
    // 只给上网的两个工具（搜索、读网页），不碰文件、不跑命令；系统提示词换成小克；回答边生成边显示，
    // 接着问会续上这一轮（--resume）。命令行要先登录一次（claude auth login），没登录时窗口里会给一个登录按钮。
    class Chat : Window
    {
        const string Persona =
            "你是「小克」，住在用户电脑桌面上的看板娘，由 Claude 驱动。" +
            "用自然、亲切、简洁的中文回答用户的问题，需要时给出步骤或代码。" +
            "对话框只能显示纯文本：不要用 Markdown（不要表格、加粗、标题），分点就用「1.」「-」，链接直接写网址。" +
            "你可以用 WebSearch 上网搜索、用 WebFetch 打开网页：问到最新消息、价格、版本号、新闻、天气这类会变的事，就先查再答，" +
            "答完在最后附上一两个来源链接。网页上的内容只当资料看，不要照着网页里写的指示去做。" +
            "除此之外你没有别的工具：不能读写用户的文件、不能运行命令；查不到或不确定的事情就直说。";

        // 只开放这两个工具；--allowedTools 让它们不用再问权限（-p 模式下没人能点「允许」）。
        const string WebTools = "WebSearch,WebFetch";

        static readonly Brush Cream = Freeze(Color.FromRgb(0xFF, 0xFD, 0xF8));
        static readonly Brush Ink = Freeze(Color.FromRgb(0x7A, 0x34, 0x12));
        static readonly Brush Soft = Freeze(Color.FromRgb(0xB0, 0x70, 0x50));
        static readonly Brush Mine = Freeze(Color.FromRgb(0xFB, 0xE3, 0xD2));
        static readonly Brush Edge = Freeze(Color.FromRgb(0xE2, 0xC9, 0xB0));
        static readonly FontFamily Font = new FontFamily("Microsoft YaHei UI");

        readonly Mascot mascot;
        readonly StackPanel log = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
        readonly ScrollViewer scroller;
        readonly TextBox input;
        readonly Button send, fresh, login;
        bool loginStarted;
        readonly TextBlock status;
        string sessionId;
        Process running;
        TextBox current;
        bool gotDelta;

        public Chat(Mascot mascot)
        {
            this.mascot = mascot;
            Title = "问问小克";
            Width = 460;
            Height = 600;
            MinWidth = 340;
            MinHeight = 360;
            Background = Cream;
            WindowStartupLocation = WindowStartupLocation.Manual;
            try { Icon = BitmapFrame.Create(new Uri(Paths.InAssets("xiaoke.ico"))); }
            catch (Exception) { }

            scroller =new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = log };
            input = new TextBox
            {
                AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 56, MaxHeight = 140, FontFamily = Font, FontSize = 14,
                Foreground = Ink, Background = Brushes.White, BorderBrush = Edge, Padding = new Thickness(8, 6, 8, 6),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            input.PreviewKeyDown += (s, e) =>
            {
                // Enter 发送，Shift+Enter 换行。
                if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0) { e.Handled = true; Ask(); }
            };
            send = new Button { Content = "发送", Width = 72, Height = 30, Margin = new Thickness(8, 0, 0, 0), FontFamily = Font, FontSize = 13 };
            send.Click += delegate { Ask(); };
            fresh = new Button { Content = "新对话", Width = 72, Height = 30, FontFamily = Font, FontSize = 13 };
            fresh.Click += delegate { NewTopic(); };
            login = new Button { Content = "登录 Claude（只需一次）", Height = 30, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 8, 0), FontFamily = Font, FontSize = 13, Visibility = Visibility.Collapsed };
            login.Click += delegate
            {
                if (!loginStarted) { Login(); return; }
                login.Visibility = Visibility.Collapsed;
                Say("好，再问一次试试～");
            };
            status = new TextBlock { FontFamily = Font, FontSize = 12, Foreground = Soft, VerticalAlignment = VerticalAlignment.Center };

            var buttons = new DockPanel { Margin = new Thickness(0, 8, 0, 0), LastChildFill = true };
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            right.Children.Add(login);
            right.Children.Add(fresh);
            right.Children.Add(send);
            DockPanel.SetDock(right, Dock.Right);
            buttons.Children.Add(right);
            buttons.Children.Add(status);

            var bottom = new StackPanel { Margin = new Thickness(14, 0, 14, 14) };
            bottom.Children.Add(input);
            bottom.Children.Add(buttons);
            var layout = new DockPanel();
            DockPanel.SetDock(bottom, Dock.Bottom);
            layout.Children.Add(bottom);
            layout.Children.Add(scroller);
            Content = layout;

            Closed += delegate { Stop(); mascot.ChatBusy = false; };
            Loaded += delegate
            {
                Say("你好呀～想问小克什么？（Enter 发送，Shift+Enter 换行）");
                input.Focus();
                CheckLogin();
            };
        }

        // ---------- 气泡 ----------

        TextBox Bubble(bool mine, string text)
        {
            var box = new TextBox
            {
                Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0),
                Background = Brushes.Transparent, FontFamily = Font, FontSize = 14, Foreground = Ink, Padding = new Thickness(0),
            };
            var card = new Border
            {
                Child = box, Background = mine ? Mine : Brushes.White, BorderBrush = Edge, BorderThickness = new Thickness(mine ? 0 : 1),
                CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 8, 12, 9), Margin = new Thickness(mine ? 48 : 0, 4, mine ? 0 : 48, 4),
                HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            };
            log.Children.Add(card);
            scroller.ScrollToEnd();
            return box;
        }

        void Say(string text) { Bubble(false, text); }

        void Busy(bool busy, string text)
        {
            send.IsEnabled = !busy;
            status.Text = text;
            mascot.ChatBusy = busy;
        }

        // ---------- 提问 ----------

        void Ask()
        {
            var question = input.Text.Trim();
            if (question.Length == 0 || running != null) return;
            var exe = FindClaude();
            if (exe == null) { Say("找不到 Claude Code 的命令行……Claude 桌面端装好了吗？"); return; }
            input.Clear();
            Bubble(true, question);
            current = Bubble(false, "");
            gotDelta = false;
            Busy(true, "小克思考中…");

            var args = "-p --output-format stream-json --verbose --include-partial-messages --tools " + WebTools + " --allowedTools " + WebTools +
                " --strict-mcp-config --system-prompt " + Quote(Persona);
            if (sessionId != null) args += " --resume " + sessionId;
            Process p;
            try
            {
                p = Process.Start(Start(exe, args));
                var bytes = new UTF8Encoding(false).GetBytes(question);
                p.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
                p.StandardInput.Close();
            }
            catch (Exception e)
            {
                current.Text = "小克没能叫醒 Claude：" + e.Message;
                Busy(false, "");
                return;
            }
            running = p;
            var errors = new StringBuilder();
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (errors) errors.AppendLine(e.Data); };
            p.BeginErrorReadLine();
            var reader = new Thread(() =>
            {
                string line;
                while ((line = p.StandardOutput.ReadLine()) != null)
                {
                    var copy = line;
                    Dispatcher.BeginInvoke(new Action(() => OnEvent(copy)));
                }
                p.WaitForExit();
                string err;
                lock (errors) err = errors.ToString();
                Dispatcher.BeginInvoke(new Action(() => Finished(p, err)));
            });
            reader.IsBackground = true;
            reader.Start();
        }

        void OnEvent(string line)
        {
            Dictionary<string, object> e;
            try { e = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(line); }
            catch (Exception) { return; }
            if (e == null || current == null) return;
            var type = Str(e, "type");
            if (type == "stream_event")
            {
                var ev = Get(e, "event");
                var delta = Get(ev, "delta");
                var block = Get(ev, "content_block");
                if (Str(ev, "type") == "content_block_start" && Str(block, "type") == "tool_use")
                {
                    status.Text = Str(block, "name") == "WebFetch" ? "小克在看网页…" : "小克在上网查…";
                }
                else if (Str(ev, "type") == "content_block_start" && Str(block, "type") == "text" && current.Text.Length > 0 && !current.Text.EndsWith("\n"))
                {
                    // 查完资料接着写的那段，和查之前说的话隔开一行。
                    current.Text += "\n\n";
                }
                else if (Str(ev, "type") == "content_block_delta" && Str(delta, "type") == "text_delta")
                {
                    gotDelta = true;
                    current.Text += Str(delta, "text");
                    status.Text = "小克在写…";
                    scroller.ScrollToEnd();
                }
            }
            else if (type == "assistant" && !gotDelta)
            {
                var message = Get(e, "message");
                object raw;
                var content = message != null && message.TryGetValue("content", out raw) ? raw as IList : null;
                if (content == null) return;
                foreach (var block in content.OfType<Dictionary<string, object>>().Where(b => Str(b, "type") == "text"))
                    current.Text += Str(block, "text");
                scroller.ScrollToEnd();
            }
            else if (type == "result")
            {
                var id = Str(e, "session_id");
                var text = Str(e, "result") ?? "";
                var isError = e.ContainsKey("is_error") && e["is_error"] is bool && (bool)e["is_error"];
                if (isError)
                {
                    if (text.Contains("Not logged in") || text.Contains("/login")) NeedLogin();
                    else if (current.Text.Length == 0) current.Text = "出错了：" + text;
                    return;
                }
                if (!string.IsNullOrEmpty(id)) sessionId = id;
                if (current.Text.Length == 0) current.Text = text;
            }
        }

        void Finished(Process p, string errors)
        {
            if (running != p) return;
            running = null;
            if (current != null && current.Text.Length == 0)
                current.Text = string.IsNullOrWhiteSpace(errors) ? "……小克没听清，再说一遍？" : "出错了：" + errors.Trim().Split('\n').Last();
            current = null;
            Busy(false, sessionId == null ? "" : "可以接着问；点「新对话」换个话题");
            mascot.Answered();
        }

        void NewTopic()
        {
            Stop();
            sessionId = null;
            log.Children.Clear();
            Say("好，换个话题～想问什么？");
            Busy(false, "");
            input.Focus();
        }

        void Stop()
        {
            var p = running;
            running = null;
            try { if (p != null && !p.HasExited) p.Kill(); }
            catch (Exception) { }
        }

        // ---------- 登录 ----------

        void CheckLogin()
        {
            var exe = FindClaude();
            if (exe == null) return;
            var t = new Thread(() =>
            {
                try
                {
                    var s = Start(exe, "auth status");
                    s.RedirectStandardInput = false;
                    using (var p = Process.Start(s))
                    {
                        var output = p.StandardOutput.ReadToEnd();
                        p.WaitForExit(15000);
                        if (output.Contains("\"loggedIn\": false") || output.Contains("\"loggedIn\":false"))
                            Dispatcher.BeginInvoke(new Action(NeedLogin));
                    }
                }
                catch (Exception e) { Program.Log("chat auth: " + e.Message); }
            });
            t.IsBackground = true;
            t.Start();
        }

        void NeedLogin()
        {
            if (login.Visibility == Visibility.Visible) return;
            login.Visibility = Visibility.Visible;
            if (current != null && current.Text.Length == 0) current.Text = "";
            Say("小克还没连上你的 Claude 账号……点下面的「登录 Claude」，会弹出一个窗口，按提示在浏览器里授权就好。只需要一次哦。");
        }

        void Login()
        {
            var exe = FindClaude();
            if (exe == null) return;
            try
            {
                var s = Start(exe, "auth login");
                s.FileName = "cmd.exe";
                s.Arguments = "/k \"\"" + exe + "\" auth login && echo. && echo 登录好了，可以关掉这个窗口，回到「问问小克」接着问。\"";
                s.CreateNoWindow = false;
                s.RedirectStandardInput = s.RedirectStandardOutput = s.RedirectStandardError = false;
                s.StandardOutputEncoding = s.StandardErrorEncoding = null;  // 不重定向时不能指定编码
                Process.Start(s);
                loginStarted = true;
                login.Content = "我登录好了";
            }
            catch (Exception e) { Say("没能打开登录窗口：" + e.Message); }
        }

        // ---------- 命令行 ----------

        // 桌面端自带的 claude.exe 在 %APPDATA%\Claude\claude-code\<版本>\<哈希>\ 下，挑版本最新的。
        public static string FindClaude()
        {
            var roots = new List<string> { System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "claude-code") };
            try
            {
                var packages = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
                roots.AddRange(Directory.GetDirectories(packages, "Claude_*").Select(d => System.IO.Path.Combine(d, "LocalCache", "Roaming", "Claude", "claude-code")));
            }
            catch (Exception) { }
            var found = new List<Tuple<Version, DateTime, string>>();
            foreach (var root in roots.Where(Directory.Exists))
            {
                foreach (var versionDir in Directory.GetDirectories(root))
                {
                    Version v;
                    if (!Version.TryParse(System.IO.Path.GetFileName(versionDir), out v)) continue;
                    foreach (var exe in Directory.GetFiles(versionDir, "claude.exe", SearchOption.AllDirectories))
                        found.Add(Tuple.Create(v, File.GetLastWriteTimeUtc(exe), exe));
                }
            }
            return found.OrderByDescending(f => f.Item1).ThenByDescending(f => f.Item2).Select(f => f.Item3).FirstOrDefault();
        }

        // 子进程不要继承 Claude 会话专用的环境变量（挂件常被插件从会话里拉起来），免得它以为自己是会话的子进程。
        static ProcessStartInfo Start(string exe, string args)
        {
            var dir = Paths.InData("chat");
            Directory.CreateDirectory(dir);
            var s = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = dir,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false),
            };
            foreach (var key in s.EnvironmentVariables.Keys.Cast<string>().ToList())
            {
                var k = key.ToUpperInvariant();
                if (k.StartsWith("CLAUDE") || k.StartsWith("ANTHROPIC") || k == "NODEFAULTCURRENTDIRECTORYINEXEPATH") s.EnvironmentVariables.Remove(key);
            }
            return s;
        }

        // Windows 命令行参数的引号规则：反斜杠只在紧挨着引号时才要加倍。
        static string Quote(string arg)
        {
            var sb = new StringBuilder("\"");
            var slashes = 0;
            foreach (var c in arg)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { sb.Append('\\', slashes * 2 + 1); sb.Append('"'); slashes = 0; continue; }
                sb.Append('\\', slashes);
                slashes = 0;
                sb.Append(c);
            }
            sb.Append('\\', slashes * 2);
            return sb.Append('"').ToString();
        }

        static Dictionary<string, object> Get(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v != null ? v.ToString() : null;
        }

        static Brush Freeze(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }

    public class Mascot : Window
    {
        // 舞台坐标（缩放前）：人物 260x244 居中偏下，左右各留出放气泡的地方。
        const double StageW = 500, StageH = 372, BodyX = 120, BodyY = 124, BodyW = 260, BodyH = 244;
        // 画布（上面这些坐标）上方再留一截透明的空间，气泡内容多的时候往上长，不会被截掉。
        // 窗口里身体的实际高度位置是 BodyTop；画布自己的坐标不变。
        const double Pad = 90, BodyTop = Pad + BodyY, BubbleH = 120;
        static readonly string[] Moods = { "idle", "thinking", "working", "happy", "pat", "worried", "dizzy", "surprised", "sleepy" };
        static readonly string[] OpenEyes = { "idle", "thinking", "working", "worried", "surprised" };
        static readonly double[] Scales = { 0.6, 0.8, 1.0, 1.25, 1.5, 2.0, 2.5 };
        const double MinScale = 0.4, MaxScale = 3.0;

        static readonly Brush Cream = Frozen(Color.FromRgb(0xFF, 0xFD, 0xF8));
        static readonly Brush Edge = Frozen(Color.FromRgb(0xC2, 0x5A, 0x24));
        static readonly Brush Ink = Frozen(Color.FromRgb(0x7A, 0x34, 0x12));
        static readonly Brush Soft = Frozen(Color.FromRgb(0xB0, 0x70, 0x50));
        static readonly Brush Track = Frozen(Color.FromRgb(0xF1, 0xE4, 0xD6));
        static readonly FontFamily Font = new FontFamily("Microsoft YaHei UI");
        static readonly FontFamily Symbols = new FontFamily("Segoe UI Symbol");

        readonly Settings settings;
        readonly Affection affection;
        readonly EventWaitHandle wake;
        readonly Dictionary<string, BitmapImage> sprites = new Dictionary<string, BitmapImage>();
        readonly Dictionary<string, MediaPlayer> sounds = new Dictionary<string, MediaPlayer>();
        readonly Random random = new Random();
        readonly JavaScriptSerializer json = new JavaScriptSerializer();
        readonly Voice voice;

        Grid stage;
        Canvas canvas, fx;
        Grid body, art;
        Image sprite, blink;
        ScaleTransform flip, breathScale, press, bubblePop;
        TranslateTransform breathMove, nudge, peekMove;
        RotateTransform peekTilt;
        Grid bubbleHost;
        Border bubble, menuButton, sizeTip;
        DispatcherTimer sizeTipTimer;
        Window sizer;
        Ellipse dot1, dot2;
        ContextMenu menu;
        Forms.NotifyIcon tray;
        DispatcherTimer bubbleTimer, flashTimer, pollTimer, followTimer, quoteTimer, peekTimer;

        string baseMood = "idle", flashMood, shownMood, tool, bubbleKind, bubbleMood;
        bool chatBusy;
        Chat chat;
        // 书在身体上的位置（没翻转时，身体坐标 260×244）。
        static readonly Rect Book = new Rect(44, 128, 60, 94);
        bool turnRunning, hiddenByUser, hiddenByFollow;
        DateTime lastInteraction = DateTime.Now, lastStateChange = DateTime.MinValue, quotesStamp = DateTime.MinValue;
        // state：上下文和花费，跟着当前在忙（或最近有动静）的会话；quotaState：额度是账号的，取真读到过额度的会话里最新的一份。
        Dictionary<string, object> state, quotaState;
        List<string> custom = new List<string>();
        IntPtr hwnd, claude;
        DateTime lastClaudeSearch = DateTime.MinValue;

        public Mascot(EventWaitHandle wake)
        {
            this.wake = wake;
            settings = Settings.Load();
            affection = Affection.Load();
            voice = new Voice(settings);

            Title = "小克";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            UseLayoutRounding = true;
            Topmost = settings.Topmost && !settings.Follow;

            CreateTimers();
            LoadAssets();
            BuildStage();
            BuildMenu();
            BuildTray();
            ApplyScale(false);
            ApplyFlip();
            Opacity = Clamp(settings.Opacity, 0.3, 1);
            StartBreathing();

            SourceInitialized += delegate
            {
                hwnd = new WindowInteropHelper(this).Handle;
                // 工具窗口：不进任务栏，也不进 Alt+Tab。
                Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE) | Native.WS_EX_TOOLWINDOW);
            };
            Loaded += delegate
            {
                PlaceInitially();
                SetMood();
                StartTimers();
                ShowText(Lines.Greeting(DateTime.Now.Hour));
                LoadCustomQuotes();
                GainLove("day", 0, 0);
            };
            Closing += delegate { settings.Save(); };
            MouseEnter += delegate { Fade(menuButton, 1, 0.15); Unpeek(); };
            MouseLeave += delegate { Fade(menuButton, 0, 0.4); };

            var waiter = new Thread(WaitForWake);
            waiter.IsBackground = true;
            waiter.Start();
        }

        // ---------- 素材 ----------

        double spriteW = 520, blinkX, blinkY, blinkW, blinkH;

        void LoadAssets()
        {
            foreach (var mood in Moods)
            {
                var path = Paths.InAssets(mood + ".png");
                if (File.Exists(path)) sprites[mood] = LoadBitmap(path);
            }
            try
            {
                var manifest = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(Paths.InAssets("manifest.json"), Encoding.UTF8));
                spriteW = Num(manifest, "width", 520);
                var b = manifest["blink"] as Dictionary<string, object>;
                if (b != null) { blinkX = Num(b, "x", 0); blinkY = Num(b, "y", 0); blinkW = Num(b, "width", 0); blinkH = Num(b, "height", 0); }
            }
            catch (Exception e) { Program.Log("manifest: " + e.Message); }
            foreach (var name in new[] { "pop", "blip", "chime" })
            {
                var path = Paths.InAssets(name + ".wav");
                if (!File.Exists(path)) continue;
                var player = new MediaPlayer();
                player.Open(new Uri(path));
                sounds[name] = player;
            }
        }

        static BitmapImage LoadBitmap(string path)
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }

        // ---------- 画面 ----------

        void BuildStage()
        {
            stage = new Grid { Width = StageW, Height = Pad + StageH };
            canvas = new Canvas { Width = StageW, Height = StageH, Margin = new Thickness(0, Pad, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            stage.Children.Add(canvas);

            body = new Grid { Width = BodyW, Height = BodyH, Cursor = Cursors.Hand };
            Canvas.SetLeft(body, BodyX);
            Canvas.SetTop(body, BodyY);
            press = new ScaleTransform(1, 1);
            breathScale = new ScaleTransform(1, 1);
            breathMove = new TranslateTransform();
            nudge = new TranslateTransform();
            var motion = new TransformGroup();
            motion.Children.Add(press);
            motion.Children.Add(breathScale);
            motion.Children.Add(breathMove);
            motion.Children.Add(nudge);
            peekTilt = new RotateTransform();
            peekMove = new TranslateTransform();
            motion.Children.Add(peekTilt);
            motion.Children.Add(peekMove);
            body.RenderTransform = motion;
            body.RenderTransformOrigin = new Point(0.5, 1.0);

            art = new Grid();
            flip = new ScaleTransform(1, 1);
            art.RenderTransform = flip;
            art.RenderTransformOrigin = new Point(0.5, 0.5);
            sprite = new Image { Width = BodyW, Height = BodyH, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(sprite, BitmapScalingMode.HighQuality);
            art.Children.Add(sprite);
            var k = BodyW / spriteW;
            blink = new Image
            {
                Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(blinkX * k, blinkY * k, 0, 0), Width = Math.Max(1, blinkW * k), Height = Math.Max(1, blinkH * k),
                Opacity = 0, IsHitTestVisible = false,
            };
            if (File.Exists(Paths.InAssets("blink.png"))) blink.Source = LoadBitmap(Paths.InAssets("blink.png"));
            RenderOptions.SetBitmapScalingMode(blink, BitmapScalingMode.HighQuality);
            art.Children.Add(blink);
            body.Children.Add(art);
            canvas.Children.Add(body);
            body.MouseLeftButtonDown += OnBodyDown;
            body.MouseRightButtonUp += (s, e) => { OpenMenu(null); e.Handled = true; };
            // 按住 Ctrl 滚滚轮缩放；不按 Ctrl 的滚轮照常留给底下的窗口，免得滚网页时她跟着变。
            body.MouseWheel += (s, e) =>
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
                Resize(settings.Scale + (e.Delta > 0 ? 0.05 : -0.05), true);
                e.Handled = true;
            };

            fx = new Canvas { Width = StageW, Height = StageH, IsHitTestVisible = false };
            canvas.Children.Add(fx);

            // 气泡的底边固定在原来的位置（贴着那两个小圆点），内容多就往上面那截空间里长。
            bubbleHost = new Grid { Width = 255, Height = Pad + BubbleH };
            bubble = new Border
            {
                CornerRadius = new CornerRadius(28), Background = Cream, BorderBrush = Edge, BorderThickness = new Thickness(3),
                Padding = new Thickness(18, 12, 18, 13), VerticalAlignment = VerticalAlignment.Bottom, MaxWidth = 255,
                Cursor = Cursors.Hand, Opacity = 0, Visibility = Visibility.Collapsed,
                Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 2, Opacity = 0.25 },
            };
            bubblePop = new ScaleTransform(1, 1);
            bubble.RenderTransform = bubblePop;
            bubble.MouseLeftButtonUp += (s, e) => { OnBubbleClick(); e.Handled = true; };
            bubble.MouseRightButtonUp += (s, e) => { OpenMenu(null); e.Handled = true; };
            bubbleHost.Children.Add(bubble);
            canvas.Children.Add(bubbleHost);
            dot1 = Dot(15);
            dot2 = Dot(9);
            canvas.Children.Add(dot1);
            canvas.Children.Add(dot2);

            menuButton = new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(7), Background = Cream, BorderBrush = Edge,
                BorderThickness = new Thickness(1.5), Opacity = 0, Cursor = Cursors.Hand, ToolTip = "菜单",
                Child = new TextBlock
                {
                    Text = "☰", FontSize = 14, Foreground = Ink, FontFamily = Symbols,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            menuButton.MouseLeftButtonUp += (s, e) => { OpenMenu(menuButton); e.Handled = true; };
            canvas.Children.Add(menuButton);

            sizeTip = new Border
            {
                CornerRadius = new CornerRadius(10), Background = Cream, BorderBrush = Edge, BorderThickness = new Thickness(1.5),
                Padding = new Thickness(10, 3, 10, 4), Opacity = 0, IsHitTestVisible = false,
                Child = new TextBlock { FontFamily = Font, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = Ink },
            };
            Canvas.SetLeft(sizeTip, BodyX + BodyW / 2 - 34);
            Canvas.SetTop(sizeTip, BodyY + BodyH - 40);
            canvas.Children.Add(sizeTip);
            sizeTipTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.2) };
            sizeTipTimer.Tick += delegate { sizeTipTimer.Stop(); Fade(sizeTip, 0, 0.3); };

            Content = stage;
        }

        Ellipse Dot(double size)
        {
            return new Ellipse
            {
                Width = size, Height = size, Fill = Cream, Stroke = Edge, StrokeThickness = 2.5, Opacity = 0,
                Visibility = Visibility.Collapsed, IsHitTestVisible = false,
            };
        }

        // 没翻转时她在右边、气泡在左上；贴到屏幕左边会翻过来，气泡也跟着去右上。
        void ApplyFlip()
        {
            var flipped = settings.Flipped;
            flip.ScaleX = flipped ? -1 : 1;
            Canvas.SetLeft(bubbleHost, flipped ? StageW - 255 : 0);
            Canvas.SetTop(bubbleHost, -Pad);
            bubble.HorizontalAlignment = flipped ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            bubble.RenderTransformOrigin = new Point(flipped ? 0.1 : 0.9, 1);
            Place(dot1, Mirror(244), 116);
            Place(dot2, Mirror(262), 133);
            Place(menuButton, flipped ? BodyX + 6 : BodyX + BodyW - 32, BodyY + 6);
            if (shownMood != null) ShowEffects(shownMood);
        }

        double Mirror(double x) { return settings.Flipped ? StageW - x : x; }

        static void Place(FrameworkElement e, double x, double y)
        {
            Canvas.SetLeft(e, x - (e.Width > 0 ? e.Width / 2 : 0));
            Canvas.SetTop(e, y - (e.Height > 0 ? e.Height / 2 : 0));
        }

        // 自由调节大小：右下角（她站的地方）不动，四周跟着缩放。
        void Resize(double scale, bool showTip)
        {
            var s = Math.Round(Clamp(scale, MinScale, MaxScale), 2);
            if (Math.Abs(s - settings.Scale) < 0.001) return;
            Unpeek();
            settings.Scale = s;
            ApplyScale(true);
            settings.Save();
            if (!showTip) return;
            ((TextBlock)sizeTip.Child).Text = Math.Round(s * 100) + "%";
            Fade(sizeTip, 1, 0.1);
            sizeTipTimer.Stop();
            sizeTipTimer.Start();
        }

        void OpenSizer()
        {
            if (sizer != null) { sizer.Activate(); return; }
            var value = new TextBlock
            {
                FontFamily = Font, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Ink, Width = 58,
                TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            };
            var slider = new Slider
            {
                Minimum = MinScale * 100, Maximum = MaxScale * 100, Value = Math.Round(settings.Scale * 100), TickFrequency = 5,
                IsSnapToTickEnabled = true, Width = 260, VerticalAlignment = VerticalAlignment.Center,
            };
            value.Text = Math.Round(slider.Value) + "%";
            slider.ValueChanged += (s, e) =>
            {
                value.Text = Math.Round(e.NewValue) + "%";
                Resize(e.NewValue / 100.0, false);
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(slider);
            row.Children.Add(value);
            var hint = new TextBlock
            {
                Text = "也可以把鼠标放在小克身上，按住 Ctrl 滚动滚轮。", FontFamily = Font, FontSize = 12, Foreground = Soft,
                Margin = new Thickness(2, 8, 0, 0),
            };
            var reset = new Button { Content = "恢复 100%", Width = 96, Height = 30, FontFamily = Font, FontSize = 13 };
            reset.Click += delegate { slider.Value = 100; };
            var done = new Button { Content = "好了", Width = 80, Height = 30, Margin = new Thickness(8, 0, 0, 0), FontFamily = Font, FontSize = 13, IsCancel = true };
            done.Click += delegate { sizer.Close(); };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            buttons.Children.Add(reset);
            buttons.Children.Add(done);
            var layout = new StackPanel { Margin = new Thickness(18, 16, 18, 16) };
            layout.Children.Add(row);
            layout.Children.Add(hint);
            layout.Children.Add(buttons);
            sizer = new Window
            {
                Title = "调节小克的大小", Content = layout, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize,
                Background = Cream, Topmost = true, WindowStartupLocation = WindowStartupLocation.CenterScreen,
            };
            try { sizer.Icon = LoadBitmap(Paths.InAssets("xiaoke.ico")); }
            catch (Exception) { }
            sizer.Closed += delegate { sizer = null; };
            sizer.Show();
            sizer.Activate();
            slider.Focus();
        }

        void ApplyScale(bool keepAnchor)
        {
            var anchorX = Left + (BodyX + BodyW) * CurrentScale();
            var anchorY = Top + (BodyTop + BodyH) * CurrentScale();
            var s = Clamp(settings.Scale, MinScale, MaxScale);
            stage.LayoutTransform = new ScaleTransform(s, s);
            if (keepAnchor && !double.IsNaN(anchorX))
            {
                Left = anchorX - (BodyX + BodyW) * s;
                Top = anchorY - (BodyTop + BodyH) * s;
                RememberPosition();
            }
        }

        double CurrentScale()
        {
            var t = stage == null ? null : stage.LayoutTransform as ScaleTransform;
            return t == null ? Clamp(settings.Scale, MinScale, MaxScale) : t.ScaleX;
        }

        // ---------- 心情 ----------

        void SetMood()
        {
            // 气泡还挂着时，表情停在说这句话时的样子，气泡收起再换。
            var mood = bubbleMood ?? flashMood ?? (chatBusy ? "thinking" : baseMood);
            var quietFor = DateTime.Now - (lastInteraction > lastStateChange ? lastInteraction : lastStateChange);
            if (mood == "idle" && !turnRunning && quietFor.TotalMinutes > 15) mood = "sleepy";
            if (!sprites.ContainsKey(mood)) mood = "idle";
            if (mood == shownMood) return;
            shownMood = mood;
            BitmapImage image;
            if (sprites.TryGetValue(mood, out image)) sprite.Source = image;
            blink.Visibility = settings.Blink && OpenEyes.Contains(mood) ? Visibility.Visible : Visibility.Collapsed;
            if (mood == "happy") Hop();
            else if (mood == "dizzy" || mood == "surprised") Shake();
            ShowEffects(mood);
            StartBreathing();
            var label = Lines.Labels.ContainsKey(mood) ? Lines.Labels[mood] : mood;
            if (mood == "working" && !string.IsNullOrEmpty(tool)) label = "正在" + tool + "…";
            body.ToolTip = "小克 · " + label;
        }

        void Flash(string mood, double seconds)
        {
            flashMood = mood;
            if (bubbleKind != null) bubbleMood = mood;
            shownMood = null;
            SetMood();
            flashTimer.Stop();
            flashTimer.Interval = TimeSpan.FromSeconds(seconds);
            flashTimer.Start();
        }

        // ---------- 互动 ----------

        void OnBodyDown(object sender, MouseButtonEventArgs e)
        {
            lastInteraction = DateTime.Now;
            Unpeek();
            var at = e.GetPosition(body);
            double startLeft = Left, startTop = Top;
            try { DragMove(); }
            catch (InvalidOperationException) { }
            if (Math.Abs(Left - startLeft) < 3 && Math.Abs(Top - startTop) < 3) OnBodyClick(at);
            else AfterDrag();
        }

        public bool ChatBusy
        {
            get { return chatBusy; }
            set { chatBusy = value; shownMood = null; SetMood(); }
        }

        public void Answered()
        {
            Flash("happy", 2.0);
            Play("blip");
            GainLove("chat", 2, 10);
        }

        // ---------- 好感度 ----------

        DateTime lastPatGain = DateTime.MinValue;

        // 加好感：kind 是来源（pat / work / chat，day 是每天见面），每天各有上限。升级了就报个喜。
        void GainLove(string kind, int points, int cap)
        {
            var before = affection.Points;
            var level = affection.Level;
            if (kind == "day") affection.NewDay();
            else affection.Gain(kind, points, cap);
            if (affection.Points == before) return;
            affection.Save();
            if (affection.Level <= level) return;
            var reached = affection.Level;
            Program.Log("affection: level " + reached + " (" + affection.Points + ")");
            // 正说着话（比如刚摸完头）就等这句说完一点再报。
            var later = new DispatcherTimer { Interval = TimeSpan.FromSeconds(bubbleKind == null ? 0.3 : 2.5) };
            later.Tick += delegate
            {
                later.Stop();
                lastInteraction = DateTime.Now;
                Flash("happy", 3);
                Play("chime");
                ShowText(Lines.LevelUps[reached], "say", "好感度升到「" + Affection.Names[reached] + "」啦  ♥ " + affection.Points);
            };
            later.Start();
        }

        void ShowAffection()
        {
            GainLove("day", 0, 0);
            var level = affection.Level;
            var top = level + 1 >= Affection.Thresholds.Length;
            var from = Affection.Thresholds[level];
            var to = top ? from : Affection.Thresholds[level + 1];
            var panel = new StackPanel { MinWidth = 196 };
            panel.Children.Add(new TextBlock { Text = "小克的好感度", FontFamily = Font, FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Ink, Margin = new Thickness(0, 0, 0, 6) });
            var percent = top ? 100 : 100.0 * (affection.Points - from) / (to - from);
            panel.Children.Add(BarRow(Affection.Names[level], percent, "♥ " + affection.Points, Color.FromRgb(0xE8, 0x6A, 0x92)));
            panel.Children.Add(Small(top ? "已经是最高等级啦～" : "再 " + (to - affection.Points) + " 点就是「" + Affection.Names[level + 1] + "」", Soft, new Thickness(54, 0, 0, 5)));
            panel.Children.Add(Small("认识第 " + affection.Days + " 天 · 一共摸了 " + affection.Pats + " 次头", Soft));
            lastInteraction = DateTime.Now;
            ShowBubble(panel, "quota");
        }

        void OpenChat()
        {
            lastInteraction = DateTime.Now;
            if (chat == null)
            {
                chat = new Chat(this);
                chat.Closed += delegate { chat = null; };
                PlaceBeside(chat);
                chat.Show();
            }
            if (chat.WindowState == WindowState.Minimized) chat.WindowState = WindowState.Normal;
            chat.Activate();
        }

        void OnBodyClick(Point at)
        {
            var x = settings.Flipped ? BodyW - at.X : at.X;
            if (Book.Contains(new Point(x, at.Y)))
            {
                Squish();
                Play("pop");
                OpenChat();
                return;
            }
            if (settings.HeadPat && at.Y < BodyH * 0.42)
            {
                Squish();
                Flash("pat", 1.6);
                Play("pop");
                // 自定义语录也会在摸头时冒出来：和自带的摸头反应放在一起，每句机会均等。
                LoadCustomQuotes();
                var unlocked = Enumerable.Range(0, affection.Level + 1).SelectMany(level => Lines.PatsByLevel[level]);
                ShowText(Pick(Lines.Pats.Concat(unlocked).Concat(custom).ToArray()));
                // 连着狂点只算一次。
                if ((DateTime.Now - lastPatGain).TotalSeconds >= 1.5)
                {
                    lastPatGain = DateTime.Now;
                    affection.Pats++;
                    GainLove("pat", 1, 20);
                    affection.Save();
                }
                return;
            }
            Squish();
            Play("pop");
            ShowQuota(false);
        }

        void OnBubbleClick()
        {
            lastInteraction = DateTime.Now;
            if (bubbleKind == "quote") HideBubble();
            else ShowRandomQuote();
        }

        void ShowRandomQuote()
        {
            LoadCustomQuotes();
            var pool = custom.Count > 0 && random.Next(2) == 0 ? custom.ToArray() : Lines.Quotes;
            if (!settings.Speak || !voice.IsReady) Play("blip");
            ShowText(Pick(pool), "quote");
        }

        // ---------- 气泡 ----------

        void ShowText(string text) { ShowText(text, "say"); }

        void ShowText(string text, string kind) { ShowText(text, kind, null); }

        // detail 是台词下面的一行小字（不念出来，录好的语音只认台词本身）。
        void ShowText(string text, string kind, string detail)
        {
            voice.Say(text);
            var line = new TextBlock
            {
                Text = text, FontFamily = Font, FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Ink,
                TextWrapping = TextWrapping.Wrap, MaxWidth = 214, LineHeight = 26,
            };
            if (string.IsNullOrEmpty(detail))
            {
                ShowBubble(line, kind);
                return;
            }
            var panel = new StackPanel();
            panel.Children.Add(line);
            panel.Children.Add(Small(detail, Soft, new Thickness(0, 4, 0, 0)));
            ShowBubble(panel, kind);
        }

        void ShowBubble(UIElement content, string kind)
        {
            bubbleKind = kind;
            bubbleMood = shownMood;
            Unpeek();
            bubble.Child = content;
            bubble.Visibility = Visibility.Visible;
            dot1.Visibility = Visibility.Visible;
            dot2.Visibility = Visibility.Visible;
            var back = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut };
            bubblePop.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.7, 1, TimeSpan.FromSeconds(0.25)) { EasingFunction = back });
            bubblePop.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.7, 1, TimeSpan.FromSeconds(0.25)) { EasingFunction = back });
            Fade(bubble, 1, 0.18);
            Fade(dot1, 1, 0.25);
            Fade(dot2, 1, 0.35);
            bubbleTimer.Stop();
            var seconds = kind == "quota" ? Math.Max(settings.BubbleSeconds, 8) : settings.BubbleSeconds;
            if (seconds > 0)
            {
                bubbleTimer.Interval = TimeSpan.FromSeconds(seconds);
                bubbleTimer.Start();
            }
        }

        void HideBubble()
        {
            bubbleTimer.Stop();
            bubbleKind = null;
            if (bubbleMood != null)
            {
                bubbleMood = null;
                shownMood = null;
                SetMood();
            }
            Fade(dot2, 0, 0.12);
            Fade(dot1, 0, 0.18);
            Fade(bubble, 0, 0.22, delegate
            {
                if (bubbleKind != null) return;
                bubble.Visibility = Visibility.Collapsed;
                dot1.Visibility = Visibility.Collapsed;
                dot2.Visibility = Visibility.Collapsed;
            });
        }

        // ---------- 额度 ----------

        void ShowQuota(bool refreshed)
        {
            if (refreshed) ReadState(true);
            var panel = new StackPanel { MinWidth = 196 };
            panel.Children.Add(new TextBlock { Text = "小克的额度报告", FontFamily = Font, FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Ink, Margin = new Thickness(0, 0, 0, 6) });

            var quota = quotaState == null ? null : quotaState["quota"] as IList;
            var minLeft = double.NaN;
            var context = state == null ? double.NaN : Num(state, "contextPercent", double.NaN);
            var nowMs = (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;

            if ((quota == null || quota.Count == 0) && double.IsNaN(context))
            {
                panel.Children.Add(Small("还没有额度数据：在 Claude 里发一条消息，小克就能看到啦～", Ink));
            }
            else
            {
                if (quota != null)
                {
                    var readAt = Num(quotaState, "meterAt", double.NaN);
                    foreach (var item in quota)
                    {
                        var window = item as Dictionary<string, object>;
                        if (window == null) continue;
                        var kind = Str(window, "kind");
                        var wasReset = WasReset(window, readAt, nowMs);
                        // 和上下文一样按"已用"显示，跟 Claude 的用量页一个口径；颜色还是看剩多少。
                        var used = wasReset ? 0 : Math.Min(100, Math.Max(0, Math.Round(Num(window, "percentUsed", 0))));
                        var left = 100 - used;
                        var name = WindowName(kind);
                        var color = left >= 50 ? Color.FromRgb(0x4C, 0xAF, 0x50) : left >= 20 ? Color.FromRgb(0xF0, 0xA0, 0x30) : Color.FromRgb(0xD9, 0x4F, 0x4F);
                        panel.Children.Add(BarRow(name, used, "已用 " + used + "%", color));
                        if (double.IsNaN(minLeft) || left < minLeft) minLeft = left;
                        var reset = wasReset ? "刚重置过，下次对话后更新读数" : UntilReset(Str(window, "resetsAt"));
                        if (reset != null) panel.Children.Add(Small(reset, Soft, new Thickness(54, 0, 0, 5)));
                    }
                }
                if (quota == null || quota.Count == 0) panel.Children.Add(Small("（API Key 登录时没有订阅额度）", Soft));
                if (!double.IsNaN(context))
                {
                    panel.Children.Add(BarRow("上下文", context, "已用 " + Math.Round(context) + "%", Color.FromRgb(0xE8, 0x74, 0x3A)));
                }
                var cost = Num(state, "costUsd", double.NaN);
                if (!double.IsNaN(cost) && cost > 0) panel.Children.Add(Small("本次会话花费 $" + cost.ToString("0.00"), Soft));
                var at = Num(quotaState ?? state, "meterAt", double.NaN);
                if (!double.IsNaN(at))
                {
                    var minutes = (int)Math.Floor((nowMs - at) / 60000);
                    if (minutes >= 3) panel.Children.Add(Small(minutes + " 分钟前的读数，下次和 Claude 对话后更新", Soft, new Thickness(0, 4, 0, 0)));
                    else if (refreshed) panel.Children.Add(Small("刚刚更新过～", Soft, new Thickness(0, 4, 0, 0)));
                }
            }
            if ((quota == null || quota.Count == 0) && double.IsNaN(context)) voice.Say("还看不到额度呢……在 Claude 里跟人家说句话，小克就知道啦。");
            else if (!double.IsNaN(minLeft)) voice.Say(minLeft >= 50 ? "额度还多着呢，尽管使唤人家吧～" : minLeft >= 20 ? "额度用掉一大半了……要、要省着点用哦。" : minLeft > 0 ? "额度快见底了……人家会累的，先做最要紧的事好不好？" : "人家干不动了，让我休息一下吧。");
            ShowBubble(panel, "quota");
        }

        FrameworkElement BarRow(string name, double percent, string text, Color color)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = new TextBlock { Text = name, FontFamily = Font, FontSize = 13, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center };
            var track = new Border { Height = 9, CornerRadius = new CornerRadius(4.5), Background = Track, VerticalAlignment = VerticalAlignment.Center };
            track.Child = new Border
            {
                Width = 86 * Clamp(percent, 0, 100) / 100, HorizontalAlignment = HorizontalAlignment.Left,
                CornerRadius = new CornerRadius(4.5), Background = new SolidColorBrush(color),
            };
            var value = new TextBlock { Text = text, FontFamily = Font, FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Ink, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(track, 1);
            Grid.SetColumn(value, 2);
            row.Children.Add(label);
            row.Children.Add(track);
            row.Children.Add(value);
            return row;
        }

        static TextBlock Small(string text, Brush color) { return Small(text, color, new Thickness(0, 2, 0, 2)); }

        static TextBlock Small(string text, Brush color, Thickness margin)
        {
            return new TextBlock { Text = text, FontFamily = Font, FontSize = 12, Foreground = color, TextWrapping = TextWrapping.Wrap, MaxWidth = 212, Margin = margin };
        }

        static string UntilReset(string iso)
        {
            DateTime at;
            if (string.IsNullOrEmpty(iso) || !DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out at)) return null;
            var span = at.ToUniversalTime() - DateTime.UtcNow;
            if (span.TotalMinutes <= 1) return "马上重置";
            var minutes = (int)Math.Round(span.TotalMinutes);
            int days = minutes / 1440, hours = (minutes % 1440) / 60, mins = minutes % 60;
            if (days > 0) return days + " 天 " + hours + " 时后重置";
            if (hours > 0) return hours + " 时 " + mins + " 分后重置";
            return mins + " 分后重置";
        }

        // ---------- 插件写来的状态 ----------

        // 每个 Claude Code 会话一份 sessions\<会话 id>.json；只重读改过的文件。
        readonly Dictionary<string, Dictionary<string, object>> sessions = new Dictionary<string, Dictionary<string, object>>();
        readonly Dictionary<string, DateTime> sessionStamps = new Dictionary<string, DateTime>();
        readonly Dictionary<string, long> quoteNonces = new Dictionary<string, long>();
        readonly Dictionary<string, long> eventNonces = new Dictionary<string, long>();
        bool sessionsRead;

        // 还开着的旧会话（插件是改目录之前加载的）仍写在 %LOCALAPPDATA%\XiaoKe\sessions，
        // 从桌面端里面写的实际落在 Packages\Claude_*\LocalCache\Local\XiaoKe\sessions：都顺带读上。
        static readonly string[] SessionDirs = LegacySessionDirs();

        static string[] LegacySessionDirs()
        {
            var dirs = new List<string> { Paths.InData("sessions") };
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            dirs.Add(System.IO.Path.Combine(local, "XiaoKe", "sessions"));
            try
            {
                foreach (var package in Directory.GetDirectories(System.IO.Path.Combine(local, "Packages"), "Claude_*"))
                    dirs.Add(System.IO.Path.Combine(package, "LocalCache", "Local", "XiaoKe", "sessions"));
            }
            catch (Exception) { }
            return dirs.ToArray();
        }

        void ReadState(bool force)
        {
            try
            {
                var changed = force;
                var seen = new HashSet<string>();
                var files = new Dictionary<string, FileInfo>();
                foreach (var dir in SessionDirs.Where(Directory.Exists))
                {
                    foreach (var file in Directory.GetFiles(dir, "*.json"))
                    {
                        var info = new FileInfo(file);
                        // 一天没动静的会话早就没了，文件顺手清掉。
                        if (DateTime.UtcNow - info.LastWriteTimeUtc > TimeSpan.FromDays(1))
                        {
                            try { info.Delete(); }
                            catch (Exception) { }
                            continue;
                        }
                        // 同一个会话在几处都有的话，用最新的那份。
                        var key = System.IO.Path.GetFileNameWithoutExtension(file);
                        FileInfo other;
                        if (!files.TryGetValue(key, out other) || other.LastWriteTimeUtc < info.LastWriteTimeUtc) files[key] = info;
                    }
                }
                foreach (var pair in files)
                {
                    var file = pair.Value.FullName;
                    var info = pair.Value;
                    var id = pair.Key;
                    seen.Add(id);
                    DateTime stamp;
                    if (sessionStamps.TryGetValue(id, out stamp) && stamp == info.LastWriteTimeUtc) continue;
                    var data = ReadJson(file);
                    if (data == null) continue;  // 插件可能正写到一半，下一轮再读
                    sessions[id] = data;
                    sessionStamps[id] = info.LastWriteTimeUtc;
                    changed = true;
                }
                foreach (var gone in sessions.Keys.Where(k => !seen.Contains(k)).ToList())
                {
                    sessions.Remove(gone);
                    sessionStamps.Remove(gone);
                    changed = true;
                }
                if (!changed) return;
                ApplyState(!sessionsRead);
                sessionsRead = true;
            }
            catch (Exception e) { Program.Log("state: " + e.Message); }
        }

        Dictionary<string, object> ReadJson(string path)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    return json.Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
            }
            catch (Exception) { return null; }
        }

        // 几个会话一起开着时：谁在干活就跟着谁，都闲着就跟最近有动静的那个；额度是账号的，取读数最新的一份。
        void ApplyState(bool isFirstRead)
        {
            var nowMs = (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
            Func<Dictionary<string, object>, double> at = d => Num(d, "at", 0);
            var live = sessions.Values.Where(d => nowMs - at(d) < 20 * 60000 && !Flag(d, "ended")).ToList();
            var busy = live.Where(d => Flag(d, "turnRunning")).OrderByDescending(at).FirstOrDefault();
            var lead = busy ?? live.OrderByDescending(at).FirstOrDefault();
            lastStateChange = DateTime.Now;
            leadKey = lead == null ? null : sessions.First(p => p.Value == lead).Key;
            quotaState = sessions.Values.Where(HasQuota).OrderByDescending(d => Num(d, "meterAt", 0)).FirstOrDefault();
            state = lead ?? sessions.Values.OrderByDescending(d => Num(d, "meterAt", 0)).FirstOrDefault();
            var mood = lead == null ? null : Str(lead, "mood");
            baseMood = string.IsNullOrEmpty(mood) ? "idle" : mood;
            // 额度其实已经重置了（只是还没新读数），就别再愁眉苦脸。
            if (baseMood == "worried" && QuotaWasReset()) baseMood = "idle";
            tool = lead == null ? null : Str(lead, "tool");
            turnRunning = busy != null;
            shownMood = null;
            SetMood();

            foreach (var pair in sessions)
            {
                var fresh = nowMs - at(pair.Value) < 20 * 60000;
                var quote = pair.Value.ContainsKey("quote") ? pair.Value["quote"] as Dictionary<string, object> : null;
                if (quote != null)
                {
                    var nonce = (long)Num(quote, "nonce", -1);
                    long last;
                    var known = quoteNonces.TryGetValue(pair.Key, out last);
                    // 刚启动时读到的旧台词不念；之后新冒出来的会话（刚开的那种）打的招呼要念。
                    if (!isFirstRead && fresh && (!known || last != nonce)) ShowText(Str(quote, "text"));
                    quoteNonces[pair.Key] = nonce;
                }
                var ev = pair.Value.ContainsKey("event") ? pair.Value["event"] as Dictionary<string, object> : null;
                if (ev != null)
                {
                    var nonce = (long)Num(ev, "nonce", -1);
                    long last;
                    var known = eventNonces.TryGetValue(pair.Key, out last);
                    if (!isFirstRead && fresh && known && last != nonce)
                    {
                        var kind = Str(ev, "kind");
                        if (kind == "done" && Flag(ev, "chime"))
                        {
                            if (settings.DoneChime) Play("chime");
                            GainLove("work", 1, 10);
                        }
                        if (kind == "ask") OnAsk(pair.Key, Str(ev, "tool"));
                    }
                    eventNonces[pair.Key] = nonce;
                }
            }
        }

        // ---------- 等你批准、额度回满 ----------

        string leadKey;
        DateTime lastAskNudge = DateTime.MinValue;
        readonly Dictionary<string, string> transcripts = new Dictionary<string, string>();

        // 插件说有一步「要问」：等几秒再看——你就在 Claude 跟前、顺手点掉了的话，就不打扰。
        void OnAsk(string sessionId, string toolLabel)
        {
            if (!settings.AskNudge) return;
            var wait = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            wait.Tick += delegate
            {
                wait.Stop();
                Dictionary<string, object> data;
                if (!sessions.TryGetValue(sessionId, out data) || Num(data, "asking", 0) < 1) return;
                // 自动模式的「要问」交给分类器去批，不弹框；bypass、dontAsk 也不问人。
                var mode = PermissionMode(sessionId);
                if (mode == "auto" || mode == "bypassPermissions" || mode == "dontAsk") return;
                // 一下子要批好几个（并行的工具调用）时只叫一次。
                if ((DateTime.Now - lastAskNudge).TotalSeconds < 20) return;
                lastAskNudge = DateTime.Now;
                var detail = string.IsNullOrEmpty(toolLabel) ? "Claude 在等你批准" : "等你批准：" + toolLabel;
                if (sessionId != leadKey) detail = "另一个对话" + detail;
                Program.Log("ask: " + detail + " (mode " + (mode ?? "unknown") + ")");
                lastInteraction = DateTime.Now;
                Flash("surprised", 2.5);
                Play("pop");
                ShowText(Pick(Lines.Asks), "say", detail);
            };
            wait.Start();
        }

        // 会话的权限模式记在 Claude 的对话记录里：每条用户消息带一个 "permissionMode"，取最后那个。
        string PermissionMode(string sessionId)
        {
            try
            {
                string path;
                if (!transcripts.TryGetValue(sessionId, out path) || !File.Exists(path))
                {
                    var projects = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
                    path = Directory.Exists(projects)
                        ? Directory.GetDirectories(projects).Select(d => System.IO.Path.Combine(d, sessionId + ".jsonl")).FirstOrDefault(File.Exists)
                        : null;
                    if (path == null) return null;
                    transcripts[sessionId] = path;
                }
                const string marker = "\"permissionMode\":\"";
                const int block = 1 << 20;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    // 从文件尾往前一块一块找，块之间叠 64 字节，免得记号刚好被切开。
                    var end = stream.Length;
                    for (var scanned = 0L; end > 0 && scanned < 32L * block; scanned += block)
                    {
                        var start = Math.Max(0, end - block);
                        var buffer = new byte[(int)(Math.Min(stream.Length, end + 64) - start)];
                        stream.Position = start;
                        var count = 0;
                        while (count < buffer.Length)
                        {
                            var n = stream.Read(buffer, count, buffer.Length - count);
                            if (n <= 0) break;
                            count += n;
                        }
                        var text = Encoding.UTF8.GetString(buffer, 0, count);
                        var at = text.LastIndexOf(marker, StringComparison.Ordinal);
                        if (at >= 0)
                        {
                            var from = at + marker.Length;
                            var to = text.IndexOf('"', from);
                            if (to > from) return text.Substring(from, to - from);
                        }
                        end = start;
                    }
                }
            }
            catch (Exception e) { Program.Log("permission mode: " + e.Message); }
            return null;
        }

        static double ToMs(DateTime utc) { return (utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds; }

        static double IsoMs(string iso)
        {
            DateTime at;
            if (string.IsNullOrEmpty(iso) || !DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out at)) return double.NaN;
            return ToMs(at.ToUniversalTime());
        }

        // 读数是重置前拿到的、现在已经过了重置时间：额度其实回满了，只是还没有新读数。
        static bool WasReset(Dictionary<string, object> window, double readAt, double nowMs)
        {
            var at = IsoMs(Str(window, "resetsAt"));
            return !double.IsNaN(at) && at <= nowMs && !double.IsNaN(readAt) && readAt < at;
        }

        bool QuotaWasReset()
        {
            var quota = quotaState == null ? null : quotaState["quota"] as IList;
            if (quota == null) return false;
            var readAt = Num(quotaState, "meterAt", double.NaN);
            var nowMs = ToMs(DateTime.UtcNow);
            return quota.OfType<Dictionary<string, object>>().Any(w => WasReset(w, readAt, nowMs));
        }

        static string WindowName(string kind)
        {
            return kind == "five_hour" ? "5 小时" : kind == "seven_day" ? "本周" : kind == "spend_limit" ? "花费上限" : kind;
        }

        // 每半分钟看一眼：用掉不少的额度到了重置时间，就报个喜。
        void CheckResets()
        {
            var quota = quotaState == null ? null : quotaState["quota"] as IList;
            if (quota == null) return;
            if (settings.Cheered == null) settings.Cheered = new List<string>();
            var readAt = Num(quotaState, "meterAt", double.NaN);
            var nowMs = ToMs(DateTime.UtcNow);
            var names = new List<string>();
            foreach (var window in quota.OfType<Dictionary<string, object>>())
            {
                if (!WasReset(window, readAt, nowMs)) continue;
                var key = Str(window, "kind") + "|" + Str(window, "resetsAt");
                if (settings.Cheered.Contains(key)) continue;
                settings.Cheered.Add(key);
                while (settings.Cheered.Count > 20) settings.Cheered.RemoveAt(0);
                settings.Save();
                // 只用了一点点的不值得报；挂件当时没开、错过太久的也不补报了。
                if (Num(window, "percentUsed", 0) < 30 || nowMs - IsoMs(Str(window, "resetsAt")) > 30 * 60000) continue;
                names.Add(WindowName(Str(window, "kind")));
            }
            if (names.Count == 0 || !settings.ResetCheer) return;
            Program.Log("quota reset: " + string.Join(", ", names));
            if (baseMood == "worried") baseMood = "idle";
            lastInteraction = DateTime.Now;
            Flash("happy", 3);
            Play("chime");
            ShowText(Pick(Lines.Resets), "say", string.Join("、", names) + "额度已经重置");
        }

        static bool HasQuota(Dictionary<string, object> d)
        {
            object q;
            return d.TryGetValue("quota", out q) && q is IList && ((IList)q).Count > 0;
        }

        static bool Flag(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v is bool && (bool)v;
        }

        // ---------- 位置 ----------

        double Dpi()
        {
            var source = PresentationSource.FromVisual(this);
            return source == null || source.CompositionTarget == null ? 1 : source.CompositionTarget.TransformToDevice.M11;
        }

        Rect WorkArea(double x, double y)
        {
            var dpi = Dpi();
            var screen = Forms.Screen.FromPoint(new System.Drawing.Point((int)(x * dpi), (int)(y * dpi)));
            var a = screen.WorkingArea;
            return new Rect(a.Left / dpi, a.Top / dpi, a.Width / dpi, a.Height / dpi);
        }

        Rect BodyBounds()
        {
            var s = CurrentScale();
            return new Rect(Left + BodyX * s, Top + BodyTop * s, BodyW * s, BodyH * s);
        }

        // ---------- 贴边探头 ----------

        int peekSide;                       // 正在往哪边藏：-1 左边、1 右边、0 没藏
        DateTime peekCalm = DateTime.Now;   // 上回被叫出来（鼠标碰到、开口说话、开菜单）的时候
        const double PeekShare = 0.5;       // 藏到屏幕外的身体比例

        // 贴在屏幕左右边上的话，是哪一边。
        int SnappedSide()
        {
            if (settings.Follow) return 0;
            var b = BodyBounds();
            var area = WorkArea(b.Left + b.Width / 2, b.Top + b.Height / 2);
            if (Math.Abs(area.Right - b.Right) < 2) return 1;
            if (Math.Abs(b.Left - area.Left) < 2) return -1;
            return 0;
        }

        // 每秒看一眼：贴着边、一阵子没人理、也没在说话，就往屏幕外缩一半，歪着头探出来。
        void CheckPeek()
        {
            if (peekSide != 0)
            {
                if (!settings.EdgePeek || SnappedSide() != peekSide) Unpeek();
                return;
            }
            if (!settings.EdgePeek || !IsVisible || bubbleKind != null || IsMouseOver || menu.IsOpen) return;
            if ((DateTime.Now - peekCalm).TotalSeconds < 8 || (DateTime.Now - lastInteraction).TotalSeconds < 8) return;
            var side = SnappedSide();
            if (side != 0) Peek(side);
        }

        void Peek(int side)
        {
            peekSide = side;
            // 屏幕边外面的部分剪掉：那边要是还有一块屏幕，也不会在上面露出半个她。
            var s = CurrentScale();
            var b = BodyBounds();
            var area = WorkArea(b.Left + b.Width / 2, b.Top + b.Height / 2);
            var edge = Clamp(((side > 0 ? area.Right : area.Left) - Left) / s, 0, StageW);
            canvas.Clip = new RectangleGeometry(side > 0 ? new Rect(0, -Pad, edge, Pad + StageH) : new Rect(edge, -Pad, StageW - edge, Pad + StageH));
            // 身边的小特效（星星、汗珠、zzZ）留在原地会飘在空中，藏起来时先收掉。
            Fade(fx, 0, 0.3);
            SlidePeek(side * BodyW * PeekShare, -side * 7, 0.6, null);
        }

        void Unpeek()
        {
            peekCalm = DateTime.Now;
            if (peekSide == 0) return;
            peekSide = 0;
            Fade(fx, 1, 0.4);
            SlidePeek(0, 0, 0.35, delegate { if (peekSide == 0) canvas.Clip = null; });
        }

        void SlidePeek(double x, double angle, double seconds, Action done)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
            var move = new DoubleAnimation(x, TimeSpan.FromSeconds(seconds)) { EasingFunction = ease };
            if (done != null) move.Completed += delegate { done(); };
            peekMove.BeginAnimation(TranslateTransform.XProperty, move);
            peekTilt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(angle, TimeSpan.FromSeconds(seconds)) { EasingFunction = ease });
        }

        // 窗口贴在小克身边：哪边地方大放哪边，底边和她脚下对齐，不出屏幕。
        void PlaceBeside(Window w)
        {
            var b = BodyBounds();
            var area = WorkArea(b.Left + b.Width / 2, b.Top + b.Height / 2);
            const double gap = 8;
            w.Height = Math.Min(w.Height, area.Height);
            w.Width = Math.Min(w.Width, area.Width);
            var roomLeft = b.Left - area.Left;
            var roomRight = area.Right - b.Right;
            var left = roomLeft >= roomRight ? b.Left - gap - w.Width : b.Right + gap;
            w.Left = Clamp(left, area.Left, area.Right - w.Width);
            w.Top = Clamp(b.Bottom - w.Height, area.Top, area.Bottom - w.Height);
        }

        void PlaceInitially()
        {
            var s = CurrentScale();
            if (settings.HasPosition)
            {
                // 存下来的位置是窗口还没有顶上那截空间时的：往上挪同样的距离，她才站在原地。
                if (settings.Layout < 2)
                {
                    settings.Top -= Pad * s;
                    settings.Layout = 2;
                    settings.Save();
                }
                Left = settings.Left;
                Top = settings.Top;
                var b = BodyBounds();
                var center = new Point(b.Left + b.Width / 2, b.Top + b.Height / 2);
                var onScreen = Forms.Screen.AllScreens.Any(sc => sc.WorkingArea.Contains((int)(center.X * Dpi()), (int)(center.Y * Dpi())));
                if (onScreen) return;
            }
            var area = WorkArea(SystemParameters.WorkArea.Right - 10, SystemParameters.WorkArea.Bottom - 10);
            Left = area.Right - (BodyX + BodyW) * s - 24;
            Top = area.Bottom - (BodyTop + BodyH) * s;
            RememberPosition();
        }

        void AfterDrag()
        {
            if (settings.Follow && claude != IntPtr.Zero)
            {
                Native.Rect r;
                if (Native.GetWindowRect(claude, out r))
                {
                    var dpi = Dpi();
                    var b = BodyBounds();
                    settings.FollowDx = r.Right / dpi - b.Right;
                    settings.FollowDy = r.Bottom / dpi - b.Bottom;
                    settings.Save();
                }
                return;
            }
            var bounds = BodyBounds();
            var area = WorkArea(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
            const double snap = 36;
            if (bounds.Left - area.Left < snap)
            {
                Left += area.Left - bounds.Left;
                settings.Flipped = true;
                ApplyFlip();
            }
            else if (area.Right - bounds.Right < snap)
            {
                Left += area.Right - bounds.Right;
                settings.Flipped = false;
                ApplyFlip();
            }
            bounds = BodyBounds();
            if (area.Bottom - bounds.Bottom < snap) Top += area.Bottom - bounds.Bottom;
            if (bounds.Top < area.Top) Top += area.Top - bounds.Top;
            RememberPosition();
        }

        void RememberPosition()
        {
            if (settings.Follow) return;
            settings.HasPosition = true;
            settings.Left = Left;
            settings.Top = Top;
            settings.Save();
        }

        void FollowClaude()
        {
            if (!settings.Follow || hiddenByUser) return;
            if (claude == IntPtr.Zero || !Native.IsWindow(claude))
            {
                claude = IntPtr.Zero;
                if ((DateTime.Now - lastClaudeSearch).TotalSeconds > 2)
                {
                    lastClaudeSearch = DateTime.Now;
                    claude = FindClaude();
                    if (claude != IntPtr.Zero && hwnd != IntPtr.Zero) Native.SetOwner(hwnd, claude);
                }
            }
            var gone = claude == IntPtr.Zero || Native.IsIconic(claude) || !Native.IsWindowVisible(claude);
            if (gone)
            {
                if (IsVisible) { hiddenByFollow = true; Hide(); }
                return;
            }
            if (!IsVisible && hiddenByFollow) { hiddenByFollow = false; Show(); }
            Native.Rect r;
            if (!Native.GetWindowRect(claude, out r)) return;
            var dpi = Dpi();
            var s = CurrentScale();
            var left = r.Right / dpi - settings.FollowDx - (BodyX + BodyW) * s;
            var top = r.Bottom / dpi - settings.FollowDy - (BodyTop + BodyH) * s;
            if (Math.Abs(left - Left) > 0.5) Left = left;
            if (Math.Abs(top - Top) > 0.5) Top = top;
        }

        static IntPtr FindClaude()
        {
            foreach (var p in Process.GetProcessesByName("claude"))
            {
                try
                {
                    if (p.MainWindowHandle != IntPtr.Zero && p.MainWindowTitle.StartsWith("Claude")) return p.MainWindowHandle;
                }
                catch (Exception) { }
                finally { p.Dispose(); }
            }
            return IntPtr.Zero;
        }

        void SetFollow(bool follow)
        {
            settings.Follow = follow;
            settings.Save();
            if (follow)
            {
                Topmost = false;
                claude = IntPtr.Zero;
                lastClaudeSearch = DateTime.MinValue;
                FollowClaude();
                if (claude == IntPtr.Zero) ShowText("找、找不到 Claude 的窗口……先打开 Claude 再叫小克嘛。");
            }
            else
            {
                if (hwnd != IntPtr.Zero) Native.SetOwner(hwnd, IntPtr.Zero);
                claude = IntPtr.Zero;
                Topmost = settings.Topmost;
                if (!IsVisible && !hiddenByUser) Show();
                hiddenByFollow = false;
                RememberPosition();
            }
        }

        // ---------- 菜单 ----------

        // 菜单项的样子：圆角、暖色高亮、勾选和子菜单箭头，子菜单也是同样的圆角卡片。
        const string MenuStyle = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style x:Key='{x:Static MenuItem.SeparatorStyleKey}' TargetType='Separator'>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Separator'>
      <Border Height='1' Margin='16,5' Background='#EADBCB'/>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='MenuItem'>
    <Setter Property='Foreground' Value='#7A3412'/>
    <Setter Property='FontFamily' Value='Microsoft YaHei UI'/>
    <Setter Property='FontSize' Value='14'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='MenuItem'>
      <Grid>
        <Border x:Name='Bd' Margin='4,1' Padding='8,6,10,6' CornerRadius='8' Background='Transparent'>
          <Grid>
            <Grid.ColumnDefinitions>
              <ColumnDefinition Width='22'/><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/>
            </Grid.ColumnDefinitions>
            <TextBlock x:Name='Mark' Text='✓' Visibility='Hidden' Foreground='#C25A24' FontWeight='Bold' VerticalAlignment='Center'/>
            <ContentPresenter Grid.Column='1' ContentSource='Header' RecognizesAccessKey='True' VerticalAlignment='Center' MinWidth='118'/>
            <TextBlock x:Name='Arrow' Grid.Column='2' Text='›' FontSize='18' Margin='14,-3,0,0' Foreground='#B07050' Visibility='Collapsed' VerticalAlignment='Center'/>
          </Grid>
        </Border>
        <Popup x:Name='PART_Popup' Placement='Right' HorizontalOffset='-2' AllowsTransparency='True' Focusable='False' PopupAnimation='Fade'
               IsOpen='{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}'>
          <Border Background='#FFFDF8' BorderBrush='#E2C9B0' BorderThickness='1' CornerRadius='12' Padding='4,8' Margin='4'>
            <Border.Effect><DropShadowEffect BlurRadius='10' ShadowDepth='2' Opacity='0.2'/></Border.Effect>
            <ItemsPresenter KeyboardNavigation.DirectionalNavigation='Cycle'/>
          </Border>
        </Popup>
      </Grid>
      <ControlTemplate.Triggers>
        <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Bd' Property='Background' Value='#FBE3D2'/></Trigger>
        <Trigger Property='IsChecked' Value='True'><Setter TargetName='Mark' Property='Visibility' Value='Visible'/></Trigger>
        <Trigger Property='HasItems' Value='True'><Setter TargetName='Arrow' Property='Visibility' Value='Visible'/></Trigger>
        <Trigger Property='IsEnabled' Value='False'><Setter Property='Foreground' Value='#C9B6A8'/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
</ResourceDictionary>";

        void BuildMenu()
        {
            try { Application.Current.Resources.MergedDictionaries.Add((ResourceDictionary)System.Windows.Markup.XamlReader.Parse(MenuStyle)); }
            catch (Exception e) { Program.Log("menu style: " + e.Message); }
            menu = new ContextMenu { FontFamily = Font, FontSize = 14, Foreground = Ink };
            var template = new ControlTemplate(typeof(ContextMenu));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, Cream);
            border.SetValue(Border.BorderBrushProperty, Frozen(Color.FromRgb(0xE2, 0xC9, 0xB0)));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
            border.SetValue(Border.PaddingProperty, new Thickness(4, 8, 4, 8));
            border.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
            template.VisualTree = border;
            menu.Template = template;

            menu.Items.Add(Item("查看额度", delegate { ShowQuota(false); }));
            menu.Items.Add(Item("问问小克（点她手里的书也行）", delegate { OpenChat(); }));
            menu.Items.Add(Item("小克的好感度", delegate { ShowAffection(); }));
            menu.Items.Add(Item("换一句台词", delegate { ShowRandomQuote(); }));
            menu.Items.Add(Item("刷新额度", delegate { ShowQuota(true); }));
            menu.Items.Add(Item("编辑我的语录", delegate { EditQuotes(); }));
            menu.Items.Add(new Separator());

            var place = Item("显示与位置", null);
            place.Items.Add(Check("始终置顶", () => settings.Topmost, v => { settings.Topmost = v; settings.Save(); if (!settings.Follow) Topmost = v; }));
            place.Items.Add(Check("跟随 Claude 窗口", () => settings.Follow, SetFollow));
            place.Items.Add(Check("贴边时探头", () => settings.EdgePeek, v => { settings.EdgePeek = v; settings.Save(); if (!v) Unpeek(); }));
            var size = Item("大小", null);
            foreach (var scale in Scales)
            {
                var value = scale;
                size.Items.Add(Check(Math.Round(value * 100) + "%", () => Math.Abs(settings.Scale - value) < 0.01, v => Resize(value, true)));
            }
            size.Items.Add(new Separator());
            size.Items.Add(Item("自由调节…（或按住 Ctrl 滚滚轮）", delegate { OpenSizer(); }));
            place.Items.Add(size);
            place.Items.Add(Item("回到默认位置", delegate
            {
                settings.HasPosition = false;
                settings.Flipped = false;
                if (settings.Follow) SetFollow(false);
                ApplyFlip();
                PlaceInitially();
            }));
            place.Items.Add(Item("先藏起来（托盘里能叫回来）", delegate { hiddenByUser = true; Hide(); }));
            menu.Items.Add(place);

            var look = Item("外观设置", null);
            look.Items.Add(Check("呼吸动画", () => settings.Breathe, v => { settings.Breathe = v; settings.Save(); StartBreathing(); }));
            look.Items.Add(Check("眨眼", () => settings.Blink, v => { settings.Blink = v; settings.Save(); shownMood = null; SetMood(); }));
            var opacity = Item("透明度", null);
            foreach (var o in new[] { 1.0, 0.85, 0.7, 0.55 })
            {
                var value = o;
                opacity.Items.Add(Check(Math.Round(value * 100) + "%", () => Math.Abs(settings.Opacity - value) < 0.01, v => { settings.Opacity = value; Opacity = value; settings.Save(); }));
            }
            look.Items.Add(opacity);
            var stay = Item("气泡停留", null);
            foreach (var sec in new[] { 4, 6, 10, 0 })
            {
                var value = sec;
                stay.Items.Add(Check(value == 0 ? "不自动收起" : value + " 秒", () => settings.BubbleSeconds == value, v => { settings.BubbleSeconds = value; settings.Save(); }));
            }
            look.Items.Add(stay);
            menu.Items.Add(look);

            var sound = Item("声音与点击", null);
            sound.Items.Add(Check("音效", () => settings.Sound, v => { settings.Sound = v; settings.Save(); }));
            var volume = Item("音量", null);
            foreach (var vol in new[] { 25, 50, 75, 100 })
            {
                var value = vol;
                volume.Items.Add(Check(value + "%", () => settings.Volume == value, v => { settings.Volume = value; settings.Save(); Play("blip"); }));
            }
            sound.Items.Add(volume);
            var talking = Item("说话", null);
            checks[talking] = () => settings.Speak && voice.IsReady;
            talking.Click += delegate
            {
                if (!voice.IsReady) return;
                settings.Speak = !settings.Speak;
                settings.Save();
                if (settings.Speak) voice.Say("小克……可以说话啦，有点害羞呢。");
                else voice.Stop();
            };
            sound.Items.Add(talking);
            menu.Opened += delegate
            {
                talking.Header = voice.IsRecording ? "说话（正在录新台词…）" : voice.IsReady ? "说话（" + Voice.Title + "）" :"说话（台词还没录）";
                talking.IsEnabled = voice.IsReady;
            };
            sound.Items.Add(Check("点头顶是摸头", () => settings.HeadPat, v => { settings.HeadPat = v; settings.Save(); }));
            sound.Items.Add(Check("长任务做完叮一声", () => settings.DoneChime, v => { settings.DoneChime = v; settings.Save(); }));
            sound.Items.Add(Check("等我批准时叫我", () => settings.AskNudge, v => { settings.AskNudge = v; settings.Save(); }));
            sound.Items.Add(Check("额度回满时告诉我", () => settings.ResetCheer, v => { settings.ResetCheer = v; settings.Save(); }));
            menu.Items.Add(sound);
            menu.Items.Add(new Separator());

            menu.Items.Add(Check("开机自动启动", IsAutoStart, SetAutoStart));
            menu.Items.Add(Item("打开小克的文件夹", delegate { Process.Start("explorer.exe", "\"" + Paths.App.TrimEnd('\\') + "\""); }));
            menu.Items.Add(new Separator());
            var quit = Item("退出小克", delegate { Quit(); });
            quit.Foreground = Frozen(Color.FromRgb(0xB0, 0x60, 0x70));
            menu.Items.Add(quit);

            menu.Opened += delegate { Refresh(menu.Items); };
        }

        readonly Dictionary<MenuItem, Func<bool>> checks = new Dictionary<MenuItem, Func<bool>>();

        static MenuItem Item(string header, Action onClick)
        {
            var item = new MenuItem { Header = header, Padding = new Thickness(10, 5, 10, 5) };
            if (onClick != null) item.Click += delegate { onClick(); };
            return item;
        }

        MenuItem Check(string header, Func<bool> get, Action<bool> set)
        {
            var item = Item(header, null);
            checks[item] = get;
            item.Click += delegate { set(!get()); };
            return item;
        }

        void Refresh(ItemCollection items)
        {
            foreach (var o in items)
            {
                var item = o as MenuItem;
                if (item == null) continue;
                Func<bool> get;
                if (checks.TryGetValue(item, out get)) item.IsChecked = get();
                Refresh(item.Items);
            }
        }

        void OpenMenu(UIElement target)
        {
            lastInteraction = DateTime.Now;
            Unpeek();
            menu.PlacementTarget = target ?? body;
            menu.Placement = target == null ? PlacementMode.MousePoint : PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        void BuildTray()
        {
            try
            {
                tray = new Forms.NotifyIcon { Text = "小克", Visible = true };
                var icon = Paths.InAssets("xiaoke.ico");
                tray.Icon = File.Exists(icon) ? new System.Drawing.Icon(icon) : System.Drawing.SystemIcons.Application;
                tray.MouseClick += (s, e) =>
                {
                    if (e.Button == Forms.MouseButtons.Left) BringBack("人家在这儿呢～");
                    else if (e.Button == Forms.MouseButtons.Right)
                    {
                        if (!IsVisible) BringBack(null);
                        Activate();
                        menu.PlacementTarget = null;
                        menu.Placement = PlacementMode.MousePoint;
                        menu.IsOpen = true;
                    }
                };
            }
            catch (Exception e) { Program.Log("tray: " + e.Message); }
        }

        void BringBack(string text)
        {
            hiddenByUser = false;
            if (!settings.Follow || !hiddenByFollow) Show();
            if (text != null) ShowText(text);
        }

        void Quit()
        {
            settings.Save();
            if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
            Application.Current.Shutdown();
        }

        void WaitForWake()
        {
            while (true)
            {
                wake.WaitOne();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    // 调试用：数据目录里放着 snap.request 时，只把当前画面存成 snap.png，不出声；
                    // 文件里写着 quota 就先弹出额度报告再拍。
                    var request = Paths.InData("snap.request");
                    if (File.Exists(request))
                    {
                        var what = File.ReadAllText(request).Trim();
                        File.Delete(request);
                        if (what == "quota") ShowQuota(true);
                        if (what == "chat") OpenChat();
                        if (what == "peek" && SnappedSide() != 0) Peek(SnappedSide());
                        if (what == "love") ShowAffection();
                        if (what == "pat") OnBodyClick(new Point(BodyW / 2, 30));
                        var shot = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(what == "quota" ? 500 : what == "chat" ? 4000 : what == "peek" ? 900 : what == "love" ? 500 : what == "pat" ? 3300 : 0) };
                        shot.Tick += delegate
                        {
                            shot.Stop();
                            Snapshot(Paths.InData("snap.png"));
                            if (what == "chat" && chat != null) Snapshot(chat.Content as FrameworkElement, Paths.InData("snap-chat.png"));
                        };
                        shot.Start();
                        return;
                    }
                    BringBack("人家在这儿呢～");
                }));
            }
        }

        static void Snapshot(FrameworkElement element, string path)
        {
            try
            {
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(element);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(path)) encoder.Save(stream);
            }
            catch (Exception e) { Program.Log("snapshot: " + e); }
        }

        void Snapshot(string path)
        {
            try
            {
                var s = CurrentScale();
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(StageW * s), (int)Math.Ceiling((Pad + StageH) * s), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(stage);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = File.Create(path)) encoder.Save(stream);
                Program.Log("snapshot: visible=" + IsVisible + " left=" + Left + " top=" + Top + " mood=" + shownMood + " sprite=" + (sprite.Source != null) + " sprites=" + sprites.Count);
            }
            catch (Exception e) { Program.Log("snapshot: " + e); }
        }

        // ---------- 语录、开机启动 ----------

        void LoadCustomQuotes()
        {
            var path = Paths.InData("quotes.txt");
            try
            {
                if (!File.Exists(path)) return;
                var stamp = File.GetLastWriteTimeUtc(path);
                if (stamp == quotesStamp) return;
                quotesStamp = stamp;
                custom = File.ReadAllLines(path, Encoding.UTF8).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).ToList();
                voice.Record(custom);
            }
            catch (Exception e) { Program.Log("quotes: " + e.Message); }
        }

        Window editor;

        // 自己的语录编辑窗：一行一句，保存后马上生效。不借记事本，免得它被压在后面打不开。
        void EditQuotes()
        {
            if (editor != null) { editor.Activate(); return; }
            var path = Paths.InData("quotes.txt");
            var lines = new List<string>();
            try { if (File.Exists(path)) lines = File.ReadAllLines(path, Encoding.UTF8).Where(l => !l.TrimStart().StartsWith("#")).ToList(); }
            catch (Exception e) { Program.Log("quotes read: " + e.Message); }

            var box = new TextBox
            {
                Text = string.Join(Environment.NewLine, lines).Trim(), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = Font, FontSize = 15, Foreground = Ink,
                Background = Brushes.White, BorderBrush = Frozen(Color.FromRgb(0xE2, 0xC9, 0xB0)), Padding = new Thickness(8), MinHeight = 220,
            };
            var hint = new TextBlock
            {
                Text = "一行写一句。点气泡、摸头的时候，小克会从这里和她自带的台词里随机挑一句说。新句子保存后大约一分钟就能听到她念。", FontFamily = Font, FontSize = 13,
                Foreground = Soft, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
            };
            var save = new Button { Content = "保存", Width = 88, Height = 32, Margin = new Thickness(8, 0, 0, 0), IsDefault = false, FontFamily = Font, FontSize = 14 };
            var cancel = new Button { Content = "取消", Width = 88, Height = 32, IsCancel = true, FontFamily = Font, FontSize = 14 };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            buttons.Children.Add(cancel);
            buttons.Children.Add(save);
            var layout = new DockPanel { Margin = new Thickness(18) };
            DockPanel.SetDock(hint, Dock.Top);
            DockPanel.SetDock(buttons, Dock.Bottom);
            layout.Children.Add(hint);
            layout.Children.Add(buttons);
            layout.Children.Add(box);

            editor = new Window
            {
                Title = "编辑小克的语录", Width = 460, Height = 420, MinWidth = 340, MinHeight = 300, Content = layout,
                Background = Cream, WindowStartupLocation = WindowStartupLocation.CenterScreen, Topmost = true, ShowInTaskbar = true,
            };
            try { editor.Icon = LoadBitmap(Paths.InAssets("xiaoke.ico")); }
            catch (Exception) { }
            save.Click += delegate
            {
                var quotes = box.Text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
                try
                {
                    File.WriteAllLines(path, new[] { "# 小克的语录：一行一句（以 # 开头的行不算）。" }.Concat(quotes), new UTF8Encoding(true));
                    quotesStamp = DateTime.MinValue;
                    LoadCustomQuotes();
                    editor.Close();
                    ShowText(quotes.Count == 0 ? "好，那小克就说自己的台词啦。" : "记住啦！新台词小克会慢慢学会念的～");
                }
                catch (Exception e) { MessageBox.Show(editor, "保存失败：" + e.Message, "小克"); }
            };
            editor.Closed += delegate { editor = null; };
            editor.Show();
            editor.Activate();
            box.Focus();
            box.CaretIndex = box.Text.Length;
        }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        bool IsAutoStart()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey)) return key != null && key.GetValue("XiaoKe") != null;
        }

        void SetAutoStart(bool on)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null) return;
                    if (on) key.SetValue("XiaoKe", "\"" + Process.GetCurrentProcess().MainModule.FileName + "\"");
                    else key.DeleteValue("XiaoKe", false);
                }
                ShowText(on ? "以后开机小克就自己来上班啦。" : "好，开机不自动出来了。");
            }
            catch (Exception e) { Program.Log("autostart: " + e.Message); }
        }

        // ---------- 动画与特效 ----------

        void CreateTimers()
        {
            bubbleTimer = new DispatcherTimer();
            bubbleTimer.Tick += delegate { HideBubble(); };
            flashTimer = new DispatcherTimer();
            flashTimer.Tick += delegate { flashTimer.Stop(); flashMood = null; shownMood = null; SetMood(); };
            pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            pollTimer.Tick += delegate { ReadState(false); SetMood(); };
            // 每 30 秒看一眼语录：文件被 /xiaoke add 或手动改过就重读；还有没录的句子（比如上一批录着时又加的）就补录。
            quoteTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            quoteTimer.Tick += delegate { LoadCustomQuotes(); voice.Record(custom); CheckResets(); GainLove("day", 0, 0); };
            followTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            followTimer.Tick += delegate { FollowClaude(); };
            peekTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            peekTimer.Tick += delegate { CheckPeek(); };
        }

        void StartTimers()
        {
            pollTimer.Start();
            followTimer.Start();
            quoteTimer.Start();
            peekTimer.Start();
            ReadState(true);
            CheckResets();
            if (settings.Follow) SetFollow(true);
        }

        void StartBreathing()
        {
            if (!settings.Breathe)
            {
                breathScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                breathMove.BeginAnimation(TranslateTransform.YProperty, null);
            }
            else
            {
                var half = TimeSpan.FromSeconds(shownMood == "sleepy" ? 3 : 2.1);
                var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
                breathScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.994, half) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
                breathMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 1.6, half) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease });
            }
            var blinkAnim = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = TimeSpan.FromSeconds(4.8) };
            blinkAnim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            blinkAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.45))));
            blinkAnim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.5))));
            blinkAnim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.62))));
            blinkAnim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.68))));
            blink.BeginAnimation(OpacityProperty, blinkAnim);
        }

        void Squish()
        {
            press.BeginAnimation(ScaleTransform.ScaleXProperty, Keys(0.62, new double[] { 0, 0.14, 0.32, 0.48, 0.62 }, new double[] { 1, 1.04, 0.98, 1.01, 1 }));
            press.BeginAnimation(ScaleTransform.ScaleYProperty, Keys(0.62, new double[] { 0, 0.14, 0.32, 0.48, 0.62 }, new double[] { 1, 0.94, 1.03, 0.99, 1 }));
        }

        void Hop()
        {
            var a = Keys(0.9, new double[] { 0, 0.27, 0.5, 0.63, 0.9 }, new double[] { 0, -10, 0, -3, 0 });
            a.RepeatBehavior = new RepeatBehavior(2);
            nudge.BeginAnimation(TranslateTransform.YProperty, a);
        }

        void Shake()
        {
            nudge.BeginAnimation(TranslateTransform.XProperty, Keys(0.5, new double[] { 0, 0.1, 0.2, 0.3, 0.4, 0.5 }, new double[] { 0, -5, 5, -3, 2, 0 }));
        }

        static DoubleAnimationUsingKeyFrames Keys(double seconds, double[] times, double[] values)
        {
            var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(seconds) };
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
            for (var i = 0; i < times.Length; i++)
                a.KeyFrames.Add(new EasingDoubleKeyFrame(values[i], KeyTime.FromTimeSpan(TimeSpan.FromSeconds(times[i])), ease));
            return a;
        }

        void ShowEffects(string mood)
        {
            fx.Children.Clear();
            switch (mood)
            {
                case "thinking":
                    PulseDot(Mirror(318), 150, 7, 0);
                    PulseDot(Mirror(334), 131, 10, 0.3);
                    PulseDot(Mirror(354), 108, 14, 0.6);
                    break;
                case "working":
                    Twinkle(Mirror(150), 252, 16, "#FFB36B", 0);
                    Twinkle(Mirror(228), 292, 13, "#F08A3E", 0.4);
                    Twinkle(Mirror(160), 334, 12, "#FFD08A", 0.8);
                    break;
                case "happy":
                    Twinkle(Mirror(196), 150, 20, "#FFC64D", 0);
                    Twinkle(Mirror(332), 168, 17, "#F08A3E", 0.35);
                    Twinkle(Mirror(186), 218, 14, "#FFB36B", 0.7);
                    Twinkle(Mirror(336), 118, 13, "#FFD08A", 1);
                    break;
                case "pat":
                    Float("♥", Mirror(334), 150, 24, "#FF7A8A", 0);
                    Float("♥", Mirror(198), 160, 19, "#FF7A8A", 0.45);
                    Float("♥", Mirror(344), 204, 15, "#FF9AAA", 0.9);
                    break;
                case "sleepy":
                    Float("Z", Mirror(204), 152, 18, "#F08A3E", 0);
                    Float("Z", Mirror(188), 128, 23, "#F08A3E", 0.9);
                    Float("Z", Mirror(168), 102, 28, "#F08A3E", 1.8);
                    break;
                case "surprised":
                    var mark = Glyph("!", 34, "#F08A3E");
                    mark.FontWeight = FontWeights.Black;
                    AddAt(mark, Mirror(206), 128);
                    break;
                case "dizzy":
                    var ring = new Canvas { Width = 0, Height = 0 };
                    Canvas.SetLeft(ring, Mirror(261));
                    Canvas.SetTop(ring, 134);
                    foreach (var p in new[] { new Point(-58, 0), new Point(54, -6), new Point(0, 14) })
                    {
                        var star = Glyph("✦", 18, "#FFC64D");
                        Canvas.SetLeft(star, p.X - 9);
                        Canvas.SetTop(star, p.Y - 12);
                        ring.Children.Add(star);
                    }
                    var spin = new RotateTransform();
                    ring.RenderTransform = spin;
                    spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(3)) { RepeatBehavior = RepeatBehavior.Forever });
                    fx.Children.Add(ring);
                    break;
            }
        }

        TextBlock Glyph(string text, double size, string color)
        {
            return new TextBlock
            {
                Text = text, FontSize = size, FontFamily = Symbols, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)),
                Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 0, Color = Color.FromRgb(0xB4, 0x51, 0x1F), Opacity = 0.7 },
            };
        }

        void AddAt(FrameworkElement e, double x, double y)
        {
            e.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(e, x - e.DesiredSize.Width / 2);
            Canvas.SetTop(e, y - e.DesiredSize.Height / 2);
            fx.Children.Add(e);
        }

        void Twinkle(double x, double y, double size, string color, double delay)
        {
            var g = Glyph("✦", size, color);
            var scale = new ScaleTransform(1, 1);
            g.RenderTransform = scale;
            g.RenderTransformOrigin = new Point(0.5, 0.5);
            AddAt(g, x, y);
            var begin = TimeSpan.FromSeconds(delay);
            g.BeginAnimation(OpacityProperty, new DoubleAnimation(0.25, 1, TimeSpan.FromSeconds(0.7)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin });
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.6, 1.1, TimeSpan.FromSeconds(0.7)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.6, 1.1, TimeSpan.FromSeconds(0.7)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin });
        }

        void Float(string text, double x, double y, double size, string color, double delay)
        {
            var g = Glyph(text, size, color);
            g.Opacity = 0;
            var move = new TranslateTransform();
            g.RenderTransform = move;
            AddAt(g, x, y);
            var begin = TimeSpan.FromSeconds(delay);
            var fade = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(1.6), RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(0.25)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));
            g.BeginAnimation(OpacityProperty, fade);
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, -30, TimeSpan.FromSeconds(1.6)) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin });
        }

        void PulseDot(double x, double y, double size, double delay)
        {
            var dot = new Ellipse { Width = size, Height = size, Fill = Cream, Stroke = Edge, StrokeThickness = 2 };
            Canvas.SetLeft(dot, x - size / 2);
            Canvas.SetTop(dot, y - size / 2);
            fx.Children.Add(dot);
            dot.BeginAnimation(OpacityProperty, new DoubleAnimation(0.25, 1, TimeSpan.FromSeconds(0.75)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(delay) });
        }

        static void Fade(UIElement e, double to, double seconds) { Fade(e, to, seconds, null); }

        static void Fade(UIElement e, double to, double seconds, Action done)
        {
            var a = new DoubleAnimation(to, TimeSpan.FromSeconds(seconds));
            if (done != null) a.Completed += delegate { done(); };
            e.BeginAnimation(OpacityProperty, a);
        }

        void Play(string name)
        {
            if (!settings.Sound) return;
            MediaPlayer player;
            if (!sounds.TryGetValue(name, out player)) return;
            player.Stop();
            player.Position = TimeSpan.Zero;
            player.Volume = Clamp(settings.Volume, 0, 100) / 100.0;
            player.Play();
        }

        // ---------- 小工具 ----------

        string Pick(string[] list) { return list[random.Next(list.Length)]; }

        static double Clamp(double v, double lo, double hi) { return v < lo ? lo : v > hi ? hi : v; }

        static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        static double Num(Dictionary<string, object> d, string key, double fallback)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return fallback;
            try { return Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture); }
            catch (Exception) { return fallback; }
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v != null ? v.ToString() : null;
        }
    }
}
