// HotspotTray - 윈도우 모바일 핫스팟 트레이 토글
// .NET Framework 4.x + WinRT (Windows.Networking.NetworkOperators)
// 외부 SDK/패키지 없이 윈도우 내장 csc.exe 로만 빌드됩니다.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Windows.Foundation;
using Windows.Networking.Connectivity;
using Windows.Networking.NetworkOperators;

[assembly: AssemblyTitle("HotspotTray")]
[assembly: AssemblyProduct("HotspotTray")]
[assembly: AssemblyDescription("윈도우 모바일 핫스팟 트레이 토글")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace HotspotTray
{
    static class App
    {
        // 릴리스 시 GitHub Actions 가 태그 값으로 덮어씁니다.
        public const string Version = "1.1.0";

        public const string Owner = "13wing-boop";
        public const string Repo = "hotspot-tray";
        public const string Asset = "HotspotTray.exe";

        public const string LatestApi =
            "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases/latest";

        public static string AssetUrl(string tag)
        {
            return "https://github.com/" + Owner + "/" + Repo + "/releases/download/" + tag + "/" + Asset;
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool noAuto = false;
            int waitPid = 0;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant().TrimStart('-', '/');
                if (a == "noauto") noAuto = true;
                else if (a == "waitfor" && i + 1 < args.Length) int.TryParse(args[++i], out waitPid);
            }

            // 업데이트 직후: 이전 인스턴스가 완전히 종료될 때까지 대기
            if (waitPid != 0)
            {
                try { Process.GetProcessById(waitPid).WaitForExit(15000); }
                catch { }
            }

            bool created;
            Mutex mutex = new Mutex(true, "HotspotTray_SingleInstance_7f3a1c", out created);
            if (!created) return;   // 이미 실행 중

            // 업데이트 후 남은 이전 버전 정리
            try
            {
                string old = Application.ExecutablePath + ".old";
                if (File.Exists(old)) File.Delete(old);
            }
            catch { }

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayContext(noAuto));
            }
            finally
            {
                mutex.ReleaseMutex();
                mutex.Close();
            }
        }
    }

    enum UiState { Off, On, Busy, Error }

    class Toast
    {
        public string Title;
        public string Body;
        public ToolTipIcon Icon;
    }

    class TrayContext : ApplicationContext
    {
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool DestroyIcon(IntPtr hIcon);

        const string AppKey = @"Software\HotspotTray";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValue = "HotspotTray";

        readonly NotifyIcon ni;
        readonly ContextMenuStrip menu;
        readonly System.Windows.Forms.Timer timer;
        readonly Icon icoOn, icoOff, icoBusy, icoErr;
        readonly ToolStripMenuItem miStatus, miToggle, miCopy,
                                   miAutoRun, miAutoOn, miKeepOn, miUpdate, miAutoUpdate, miVersion, miExit;

        // 백그라운드 스레드와 공유되는 상태
        volatile bool busy;
        volatile Toast pendingToast;
        volatile string availableVersion;   // 새 버전 태그 (없으면 null)

        // 꺼짐 방지: 켜져 있는 동안은 계속 유지 대상으로 본다. 트레이에서 끌 때만 해제된다.
        volatile bool desiredOn;
        volatile int recoverResult;         // 재점등 결과: 0 없음, 1 성공, 2 실패

        UiState current = UiState.Busy;     // UI 스레드 전용
        bool idleSuppressed;                // 이번 "켜짐" 구간에서 타임아웃을 껐는지
        DateTime nextRecoverAt;             // 재시도 백오프
        int recoverFails;
        DateTime lastRecoverToast;

        public TrayContext(bool noAuto)
        {
            icoOn = MakeIcon(Color.FromArgb(0x2E, 0xC2, 0x6B));     // 켜짐: 초록
            icoOff = MakeIcon(Color.FromArgb(0x98, 0x9E, 0xA8));    // 꺼짐: 회색
            icoBusy = MakeIcon(Color.FromArgb(0xF0, 0xA0, 0x20));   // 전환 중: 주황
            icoErr = MakeIcon(Color.FromArgb(0xE0, 0x4B, 0x4B));    // 오류: 빨강

            miStatus = new ToolStripMenuItem("상태 확인 중...");
            miStatus.Enabled = false;
            miToggle = new ToolStripMenuItem("핫스팟 켜기", null, OnToggleClick);
            miToggle.Font = new Font(SystemFonts.MenuFont, FontStyle.Bold);
            miCopy = new ToolStripMenuItem("SSID · 비밀번호 복사", null, OnCopyClick);
            miAutoRun = new ToolStripMenuItem("윈도우 시작 시 자동 실행", null, OnAutoRunClick);
            miAutoOn = new ToolStripMenuItem("시작할 때 핫스팟 자동 켜기", null, OnAutoOnClick);
            miKeepOn = new ToolStripMenuItem("꺼짐 방지 (내가 끌 때까지 유지)", null, OnKeepOnClick);
            miUpdate = new ToolStripMenuItem("업데이트 확인", null, OnUpdateClick);
            miAutoUpdate = new ToolStripMenuItem("자동 업데이트 확인", null, OnAutoUpdateClick);
            miVersion = new ToolStripMenuItem("HotspotTray v" + App.Version);
            miVersion.Enabled = false;
            miExit = new ToolStripMenuItem("종료", null, OnExitClick);

            menu = new ContextMenuStrip();
            menu.Items.AddRange(new ToolStripItem[] {
                miStatus,
                new ToolStripSeparator(),
                miToggle, miCopy,
                new ToolStripSeparator(),
                miAutoRun, miAutoOn, miKeepOn,
                new ToolStripSeparator(),
                miUpdate, miAutoUpdate, miVersion,
                new ToolStripSeparator(),
                miExit });
            menu.Opening += delegate { UpdateMenu(); };

            ni = new NotifyIcon();
            ni.Icon = icoBusy;
            ni.Text = "핫스팟";
            ni.ContextMenuStrip = menu;
            ni.MouseClick += OnTrayClick;
            ni.Visible = true;

            timer = new System.Windows.Forms.Timer();
            timer.Interval = 2000;
            timer.Tick += OnTick;
            timer.Start();

            // 시작하자마자 켤 예정이면 아직 꺼져 있어도 "켬"이 사용자 의도다.
            // (StartupWorker 가 2분 30초 안에 못 켜면 이후는 감시 스레드가 이어받는다)
            bool willAutoStart = !noAuto && GetAutoOn();
            if (willAutoStart) desiredOn = true;

            RefreshState();
            if (willAutoStart) Spawn(StartupWorker);
            if (GetAutoUpdate()) Spawn(UpdateLoopWorker);
        }

        // ---------- WinRT ----------

        static NetworkOperatorTetheringManager GetManager(out string err)
        {
            err = null;
            ConnectionProfile profile = null;
            try
            {
                profile = NetworkInformation.GetInternetConnectionProfile();
                if (profile == null) { err = "인터넷 연결 없음"; return null; }
                return NetworkOperatorTetheringManager.CreateFromConnectionProfile(profile);
            }
            catch (Exception ex)
            {
                err = ex.Message;
                if (profile != null)
                {
                    try
                    {
                        string cap = NetworkOperatorTetheringManager
                            .GetTetheringCapabilityFromConnectionProfile(profile).ToString();
                        if (cap != "Enabled") err = CapText(cap);
                    }
                    catch { }
                }
                return null;
            }
        }

        static string CapText(string cap)
        {
            switch (cap)
            {
                case "DisabledByGroupPolicy": return "그룹 정책으로 차단됨";
                case "DisabledByHardwareLimitation": return "이 어댑터는 핫스팟 미지원";
                case "DisabledByOperator": return "통신사에서 차단됨";
                case "DisabledBySku": return "이 윈도우 에디션에서 미지원";
                case "DisabledByRequiredAppNotInstalled": return "필수 앱 미설치";
                default: return "사용 불가 (" + cap + ")";
            }
        }

        // IAsyncOperation 을 동기적으로 대기. (Windows SDK 의 통합 Windows.winmd 가 없어
        //  await 확장 메서드를 못 쓰므로 Completed 콜백을 직접 사용)
        static T WaitOp<T>(IAsyncOperation<T> op)
        {
            using (ManualResetEventSlim done = new ManualResetEventSlim(false))
            {
                op.Completed = delegate(IAsyncOperation<T> a, AsyncStatus s) { done.Set(); };
                done.Wait();
                return op.GetResults();
            }
        }

        // 연결된 기기가 없으면 윈도우가 약 5분 뒤 핫스팟을 끈다("전원 절약").
        // 이 타임아웃 자체를 꺼서 근본적으로 막는다. 매니저 인스턴스가 아니라
        // 시스템 전역 설정이라 정적 메서드이며, 핫스팟을 껐다 켜면 되살아나므로
        // 켤 때마다 다시 호출한다.
        static void SetIdleTimeout(bool enabled)
        {
            try
            {
                if (NetworkOperatorTetheringManager.IsNoConnectionsTimeoutEnabled() == enabled) return;
                if (enabled) NetworkOperatorTetheringManager.EnableNoConnectionsTimeout();
                else NetworkOperatorTetheringManager.DisableNoConnectionsTimeout();
            }
            catch { }   // 이 API 가 없는 윈도우에서는 감시 스레드가 대신 처리한다
        }

        // ---------- 백그라운드 작업 ----------

        static void Spawn(ThreadStart work)
        {
            Thread t = new Thread(work);
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.MTA);
            t.Start();
        }

        void ToggleWorker()
        {
            try
            {
                string err;
                NetworkOperatorTetheringManager mgr = GetManager(out err);
                if (mgr == null) { ShowToast("핫스팟을 사용할 수 없습니다", err, ToolTipIcon.Warning); return; }

                bool wasOn = mgr.TetheringOperationalState == TetheringOperationalState.On;
                NetworkOperatorTetheringOperationResult r = wasOn
                    ? WaitOp(mgr.StopTetheringAsync())
                    : WaitOp(mgr.StartTetheringAsync());

                if (r.Status != TetheringOperationStatus.Success)
                    ShowToast(wasOn ? "핫스팟 끄기 실패" : "핫스팟 켜기 실패",
                              r.Status.ToString() + " " + (r.AdditionalErrorMessage ?? ""),
                              ToolTipIcon.Warning);
                else if (!wasOn && GetKeepOn())
                    SetIdleTimeout(false);
            }
            catch (Exception ex) { ShowToast("핫스팟 전환 실패", ex.Message, ToolTipIcon.Warning); }
            finally { busy = false; }
        }

        // 부팅 직후엔 인터넷 연결 프로필이 아직 안 잡히므로 최대 2분 30초간 재시도
        void StartupWorker()
        {
            for (int i = 0; i < 30; i++)
            {
                try
                {
                    string err;
                    NetworkOperatorTetheringManager mgr = GetManager(out err);
                    if (mgr != null)
                    {
                        bool keep = GetKeepOn();
                        if (mgr.TetheringOperationalState == TetheringOperationalState.On)
                        {
                            if (keep) SetIdleTimeout(false);
                            return;
                        }
                        busy = true;
                        try
                        {
                            if (WaitOp(mgr.StartTetheringAsync()).Status == TetheringOperationStatus.Success)
                            {
                                if (keep) SetIdleTimeout(false);
                                return;
                            }
                        }
                        finally { busy = false; }
                    }
                }
                catch { busy = false; }
                Thread.Sleep(5000);
            }
        }

        // 꺼짐 방지: 사용자가 끄지 않았는데 꺼져 있으면 다시 켠다.
        // 무연결 타임아웃 외의 원인(절전 복귀, 어댑터 재시작, 인터넷 프로필 변경)까지 덮는다.
        void RecoverWorker()
        {
            bool ok = false;
            try
            {
                string err;
                NetworkOperatorTetheringManager mgr = GetManager(out err);
                if (mgr != null)
                {
                    ok = mgr.TetheringOperationalState != TetheringOperationalState.Off
                       || WaitOp(mgr.StartTetheringAsync()).Status == TetheringOperationStatus.Success;
                    if (ok) SetIdleTimeout(false);
                }
            }
            catch { }
            finally { recoverResult = ok ? 1 : 2; busy = false; }
        }

        // 꺼짐 방지를 켜고 끌 때 윈도우의 무연결 타임아웃도 반대로 맞춰 준다.
        // (WinRT 호출이라 UI 스레드를 막지 않도록 배경 스레드에서 실행한다)
        void IdleTimeoutWorker()
        {
            SetIdleTimeout(!GetKeepOn());
        }

        void ShowToast(string title, string body, ToolTipIcon icon)
        {
            Toast t = new Toast();
            t.Title = title;
            t.Body = body ?? "";
            t.Icon = icon;
            pendingToast = t;   // UI 타이머가 2초 안에 집어감
        }

        // ---------- 업데이트 ----------

        static string HttpGet(string url)
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;   // TLS 1.2
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "HotspotTray/" + App.Version;
            req.Accept = "application/vnd.github+json";
            req.Timeout = 15000;
            using (WebResponse res = req.GetResponse())
            using (StreamReader sr = new StreamReader(res.GetResponseStream()))
                return sr.ReadToEnd();
        }

        static void HttpDownload(string url, string path)
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "HotspotTray/" + App.Version;
            req.Timeout = 60000;
            req.ReadWriteTimeout = 120000;
            req.AllowAutoRedirect = true;
            using (WebResponse res = req.GetResponse())
            using (Stream input = res.GetResponseStream())
            using (FileStream fs = File.Create(path))
                input.CopyTo(fs);
        }

        static string FetchLatestTag()
        {
            Match m = Regex.Match(HttpGet(App.LatestApi), "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        static bool IsNewer(string tag, string cur)
        {
            Version a, b;
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out a)) return false;
            if (!Version.TryParse(cur, out b)) return false;
            return a > b;
        }

        // 시작 1분 뒤 첫 확인, 이후 24시간 주기
        void UpdateLoopWorker()
        {
            Thread.Sleep(60000);
            while (true)
            {
                if (GetAutoUpdate())
                {
                    try
                    {
                        string tag = FetchLatestTag();
                        if (tag != null && IsNewer(tag, App.Version) && availableVersion == null)
                        {
                            availableVersion = tag;
                            ShowToast("새 버전 " + tag, "트레이 메뉴에서 업데이트를 설치하세요", ToolTipIcon.Info);
                        }
                    }
                    catch { }
                }
                Thread.Sleep(24 * 60 * 60 * 1000);
            }
        }

        void CheckNowWorker()
        {
            try
            {
                string tag = FetchLatestTag();
                if (tag == null)
                {
                    ShowToast("업데이트 확인 실패", "릴리스 정보를 읽을 수 없습니다", ToolTipIcon.Warning);
                }
                else if (IsNewer(tag, App.Version))
                {
                    availableVersion = tag;
                    ShowToast("새 버전 " + tag, "메뉴에서 업데이트를 설치하세요", ToolTipIcon.Info);
                }
                else
                {
                    ShowToast("최신 버전입니다", "v" + App.Version, ToolTipIcon.Info);
                }
            }
            catch (Exception ex) { ShowToast("업데이트 확인 실패", ex.Message, ToolTipIcon.Warning); }
        }

        // 실행 중인 exe 는 이름 변경이 가능하다는 점을 이용해 자기 자신을 교체한다.
        void InstallUpdate()
        {
            string tag = availableVersion;
            if (tag == null) return;

            string exe = Application.ExecutablePath;
            string tmp = exe + ".new";
            string old = exe + ".old";
            try
            {
                HttpDownload(App.AssetUrl(tag), tmp);
                if (new FileInfo(tmp).Length < 4096) throw new Exception("내려받은 파일이 손상되었습니다");

                if (File.Exists(old)) File.Delete(old);
                File.Move(exe, old);
                try { File.Move(tmp, exe); }
                catch { File.Move(old, exe); throw; }   // 롤백

                Process.Start(new ProcessStartInfo(exe,
                    "/waitfor " + Process.GetCurrentProcess().Id) { UseShellExecute = false });
                OnExitClick(null, null);
            }
            catch (UnauthorizedAccessException)
            {
                Cleanup(tmp);
                ni.ShowBalloonTip(6000, "업데이트 실패",
                    "이 폴더에 쓸 권한이 없습니다. 사용자 폴더(예: %LOCALAPPDATA%\\Programs\\HotspotTray)로 옮겨 설치하세요.",
                    ToolTipIcon.Error);
            }
            catch (Exception ex)
            {
                Cleanup(tmp);
                ni.ShowBalloonTip(6000, "업데이트 실패", ex.Message, ToolTipIcon.Error);
            }
        }

        static void Cleanup(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }

        // ---------- UI (모두 UI 스레드) ----------

        void OnTick(object s, EventArgs e)
        {
            Toast t = pendingToast;
            if (t != null)
            {
                pendingToast = null;
                ni.ShowBalloonTip(4000, t.Title, t.Body, t.Icon);
            }

            int rr = recoverResult;
            if (rr != 0)
            {
                recoverResult = 0;
                if (rr == 1)
                {
                    recoverFails = 0;
                    nextRecoverAt = DateTime.MinValue;
                    // 계속 실패/성공을 반복할 때 알림이 쏟아지지 않도록 1분에 한 번만 알린다.
                    if (DateTime.UtcNow - lastRecoverToast > TimeSpan.FromMinutes(1))
                    {
                        lastRecoverToast = DateTime.UtcNow;
                        ni.ShowBalloonTip(3000, "핫스팟을 다시 켰습니다",
                                          "꺼짐 방지가 켜져 있어 자동으로 복구했습니다", ToolTipIcon.Info);
                    }
                }
                else
                {
                    // 인터넷이 없는 등 당장 켤 수 없는 상황이면 간격을 늘려 가며 재시도
                    if (recoverFails < 100) recoverFails++;
                    nextRecoverAt = DateTime.UtcNow.AddSeconds(Math.Min(60, 5 * recoverFails));
                }
            }

            RefreshState();
        }

        void RefreshState()
        {
            if (busy) { Apply(UiState.Busy, "전환 중..."); return; }

            string err;
            NetworkOperatorTetheringManager mgr = GetManager(out err);
            if (mgr == null) { Apply(UiState.Error, "사용 불가 · " + err); return; }

            string ssid = "";
            try { ssid = mgr.GetCurrentAccessPointConfiguration().Ssid; }
            catch { }
            string tail = (ssid.Length > 0) ? " · " + ssid : "";

            bool keep = GetKeepOn();
            TetheringOperationalState st = mgr.TetheringOperationalState;
            if (st == TetheringOperationalState.On)
            {
                desiredOn = true;               // 켜져 있는 동안은 유지 대상
                if (keep && !idleSuppressed) { idleSuppressed = true; Spawn(IdleTimeoutWorker); }

                int n = 0;
                try { n = (int)mgr.ClientCount; }
                catch { }
                Apply(UiState.On, "켜짐 · 연결 " + n + "대" + tail);
            }
            else if (st == TetheringOperationalState.Off)
            {
                idleSuppressed = false;
                if (desiredOn && keep && DateTime.UtcNow >= nextRecoverAt)
                {
                    busy = true;
                    Apply(UiState.Busy, "다시 켜는 중..." + tail);
                    Spawn(RecoverWorker);
                    return;
                }
                Apply(UiState.Off, "꺼짐" + tail);
            }
            else
            {
                Apply(UiState.Busy, "전환 중...");
            }
        }

        void Toggle()
        {
            if (busy) return;
            // 트레이에서 끄는 것이 "내가 껐다"의 유일한 신호다. 이때만 감시를 놓는다.
            desiredOn = (current != UiState.On);
            recoverFails = 0;
            nextRecoverAt = DateTime.MinValue;
            busy = true;
            Apply(UiState.Busy, "전환 중...");
            Spawn(ToggleWorker);
        }

        void Apply(UiState st, string text)
        {
            current = st;
            Icon ico = (st == UiState.On) ? icoOn
                     : (st == UiState.Off) ? icoOff
                     : (st == UiState.Busy) ? icoBusy : icoErr;
            if (!ReferenceEquals(ni.Icon, ico)) ni.Icon = ico;

            string t = "핫스팟 " + text;
            if (t.Length > 63) t = t.Substring(0, 60) + "...";
            if (ni.Text != t) ni.Text = t;
        }

        void UpdateMenu()
        {
            miStatus.Text = ni.Text;
            miToggle.Text = (current == UiState.On) ? "핫스팟 끄기" : "핫스팟 켜기";
            miToggle.Enabled = !busy && current != UiState.Error;
            miCopy.Enabled = current != UiState.Error;
            miAutoRun.Checked = GetAutoRun();
            miAutoOn.Checked = GetAutoOn();
            miKeepOn.Checked = GetKeepOn();
            miAutoUpdate.Checked = GetAutoUpdate();

            string av = availableVersion;
            if (av != null)
            {
                miUpdate.Text = "업데이트 설치 (" + av + ")";
                miUpdate.Font = new Font(SystemFonts.MenuFont, FontStyle.Bold);
            }
            else
            {
                miUpdate.Text = "업데이트 확인";
                miUpdate.Font = SystemFonts.MenuFont;
            }
        }

        void OnTrayClick(object s, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) Toggle();
        }

        void OnToggleClick(object s, EventArgs e) { Toggle(); }

        void OnUpdateClick(object s, EventArgs e)
        {
            if (availableVersion != null) InstallUpdate();
            else Spawn(CheckNowWorker);
        }

        void OnCopyClick(object s, EventArgs e)
        {
            string err;
            NetworkOperatorTetheringManager mgr = GetManager(out err);
            if (mgr == null) { ni.ShowBalloonTip(4000, "정보를 읽을 수 없습니다", err, ToolTipIcon.Warning); return; }
            try
            {
                NetworkOperatorTetheringAccessPointConfiguration cfg = mgr.GetCurrentAccessPointConfiguration();
                Clipboard.SetText("SSID: " + cfg.Ssid + Environment.NewLine + "PW: " + cfg.Passphrase);
                ni.ShowBalloonTip(2500, "클립보드에 복사됨", "SSID: " + cfg.Ssid, ToolTipIcon.Info);
            }
            catch (Exception ex) { ni.ShowBalloonTip(4000, "복사 실패", ex.Message, ToolTipIcon.Warning); }
        }

        void OnAutoRunClick(object s, EventArgs e) { SetAutoRun(!GetAutoRun()); }
        void OnAutoOnClick(object s, EventArgs e) { SetAutoOn(!GetAutoOn()); }
        void OnAutoUpdateClick(object s, EventArgs e) { SetAutoUpdate(!GetAutoUpdate()); }

        void OnKeepOnClick(object s, EventArgs e)
        {
            bool on = !GetKeepOn();
            SetKeepOn(on);
            // 켜면 지금 켜져 있는 상태를 유지 대상으로 삼고, 끄면 감시를 놓는다.
            desiredOn = on && current == UiState.On;
            idleSuppressed = false;
            recoverFails = 0;
            nextRecoverAt = DateTime.MinValue;
            Spawn(IdleTimeoutWorker);   // 윈도우의 무연결 타임아웃도 반대로 맞춘다
        }

        void OnExitClick(object s, EventArgs e)
        {
            timer.Stop();
            ni.Visible = false;
            ni.Dispose();
            ExitThread();
        }

        // ---------- 설정 ----------

        static bool GetAutoRun()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                return k != null && k.GetValue(RunValue) != null;
        }

        static void SetAutoRun(bool on)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (k == null) return;
                if (on) k.SetValue(RunValue, "\"" + Application.ExecutablePath + "\"", RegistryValueKind.String);
                else if (k.GetValue(RunValue) != null) k.DeleteValue(RunValue, false);
            }
        }

        static bool GetFlag(string name)
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(AppKey, false))
            {
                if (k == null) return true;   // 기본값: 켜기
                object v = k.GetValue(name);
                return v == null || Convert.ToInt32(v) != 0;
            }
        }

        static void SetFlag(string name, bool on)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(AppKey))
                if (k != null) k.SetValue(name, on ? 1 : 0, RegistryValueKind.DWord);
        }

        static bool GetAutoOn() { return GetFlag("StartHotspotOnLaunch"); }
        static void SetAutoOn(bool on) { SetFlag("StartHotspotOnLaunch", on); }
        static bool GetAutoUpdate() { return GetFlag("AutoUpdate"); }
        static void SetAutoUpdate(bool on) { SetFlag("AutoUpdate", on); }
        static bool GetKeepOn() { return GetFlag("KeepHotspotOn"); }
        static void SetKeepOn(bool on) { SetFlag("KeepHotspotOn", on); }

        // ---------- 아이콘 (런타임 생성 - 외부 .ico 파일 불필요) ----------

        static Icon MakeIcon(Color c)
        {
            Bitmap bmp = new Bitmap(32, 32);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (Pen p = new Pen(c, 3.4f))
                {
                    p.StartCap = LineCap.Round;
                    p.EndCap = LineCap.Round;
                    g.DrawArc(p, 3f, 6f, 26f, 26f, 205f, 130f);
                    g.DrawArc(p, 9f, 12f, 14f, 14f, 205f, 130f);
                }
                using (Brush b = new SolidBrush(c))
                    g.FillEllipse(b, 12f, 21f, 8f, 8f);
            }
            IntPtr h = bmp.GetHicon();
            Icon ico = (Icon)Icon.FromHandle(h).Clone();
            DestroyIcon(h);
            bmp.Dispose();
            return ico;
        }
    }
}
