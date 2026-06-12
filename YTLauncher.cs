using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Threading;
using System.Net;
using Microsoft.Win32;

namespace YTLauncher
{
    public class HotkeyWindow : Form
    {
        private const int WM_HOTKEY = 0x0312;
        private Action<int> onHotkey;

        public HotkeyWindow(Action<int> onHotkey)
        {
            this.onHotkey = onHotkey;
            this.ShowInTaskbar = false;
            this.WindowState = FormWindowState.Minimized;
            this.Visible = false;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (onHotkey != null)
                {
                    onHotkey(id);
                }
            }
            base.WndProc(ref m);
        }
    }

    public class YTApp : ApplicationContext
    {
        private NotifyIcon trayIcon;
        private string settingsFile = "settings.json";
        
        // Settings variables
        private string browser = "chrome";
        private string generalProfile = "Default";
        private string musicProfile = "Profile 1";
        private string generalUrl = "https://www.youtube.com";
        private string musicUrl = "resume";
        private bool autoHideBrowser = false;
        private bool isolateMusicBrowser = true;

        // Active YouTube window handle
        private static IntPtr musicWindowHandle = IntPtr.Zero;

        // HTTP Server variables for Chrome Extension updates
        private HttpListener listener;
        private Thread listenerThread;

        // Global hotkey window
        private HotkeyWindow hotkeyWindow;

        // Thread-safe Timer for Smart Auto-Hide Focus Monitoring
        private System.Threading.Timer focusTimer;
        private System.Threading.Timer memoryOptimizerTimer;

        // Win32 API declarations
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        // --- Win32 Toolhelp32 and Process Memory Optimization Imports ---
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct PROCESSENTRY32
        {
            public uint dwSize;
            public uint cntUsage;
            public uint th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID;
            public uint cntThreads;
            public uint th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

        private const uint TH32CS_SNAPPROCESS = 2;

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const uint PROCESS_SET_QUOTA = 0x0100;

        // Win32 Constants
        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;
        private const int SW_RESTORE = 9;
        
        private const int WM_APPCOMMAND = 0x0319;
        private const int APPCOMMAND_MEDIA_PLAY_PAUSE = 14;
        private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint WM_CLOSE = 0x0010;
        private const int WM_HOTKEY = 0x0312;

        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;

        // Hotkey IDs
        private const int HOTKEY_PLAY_PAUSE = 1;
        private const int HOTKEY_SHOW_HIDE = 2;

        private static Mutex mutex = null;

        [STAThread]
        public static void Main()
        {
            const string appName = "YTLauncherUniqueMutexName_18293";
            bool createdNew;

            mutex = new Mutex(true, appName, out createdNew);

            if (!createdNew)
            {
                // App is already running, exit silently
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new YTApp());
        }

        public YTApp()
        {
            // Initial settings load / file creation
            EnsureSettingsExist();
            LoadSettings();

            // Start HTTP Server to listen to Chrome extension updates
            StartHttpServer();

            // Initialize Thread-safe Focus Timer (Starts inactive)
            focusTimer = new System.Threading.Timer(FocusTimerCallback, null, Timeout.Infinite, Timeout.Infinite);
            memoryOptimizerTimer = new System.Threading.Timer(MemoryOptimizerCallback, null, Timeout.Infinite, Timeout.Infinite);

            // Start Hotkey message receiver window
            hotkeyWindow = new HotkeyWindow(HandleHotkey);
            RegisterHotKey(hotkeyWindow.Handle, HOTKEY_PLAY_PAUSE, MOD_CONTROL | MOD_ALT, (uint)'P');
            RegisterHotKey(hotkeyWindow.Handle, HOTKEY_SHOW_HIDE, MOD_CONTROL | MOD_ALT, (uint)'H');

            // Create tray icon
            trayIcon = new NotifyIcon();
            trayIcon.Icon = CreateDynamicIcon();
            trayIcon.Text = "YT Launcher";
            trayIcon.Visible = true;

            // Clicking tray icon opens Music profile
            trayIcon.Click += (s, e) =>
            {
                var mouseEvent = e as MouseEventArgs;
                if (mouseEvent != null && mouseEvent.Button == MouseButtons.Left)
                {
                    LaunchMusic(false);
                }
            };

            // Build classic Context Menu in restructured layout
            ContextMenu menu = new ContextMenu();
            
            // Section 1: Visibility & Playback
            MenuItem itemToggle = new MenuItem("Show/Hide Browser", (s, e) => ToggleBrowserVisibility());
            MenuItem itemPlay = new MenuItem("Pause/Resume", (s, e) => TogglePlayPause());
            
            // Section 2: Accounts
            MenuItem itemMusic = new MenuItem("YouTube (Music Account)", (s, e) => LaunchMusic(false));
            itemMusic.DefaultItem = true; // Bold/Double-click default
            MenuItem itemGeneral = new MenuItem("YouTube (General Account)", (s, e) => LaunchGeneral());
            
            // Section 3: Admin & Exit
            MenuItem itemSettings = new MenuItem("Edit Settings", (s, e) => EditSettings());
            MenuItem itemExit = new MenuItem("Exit", (s, e) => Exit());

            // Assemble Menu with separators
            menu.MenuItems.Add(itemToggle);
            menu.MenuItems.Add(itemPlay);
            menu.MenuItems.Add(new MenuItem("-")); // Separator 1
            
            menu.MenuItems.Add(itemMusic);
            menu.MenuItems.Add(itemGeneral);
            menu.MenuItems.Add(new MenuItem("-")); // Separator 2
            
            menu.MenuItems.Add(itemSettings);
            menu.MenuItems.Add(itemExit);

            trayIcon.ContextMenu = menu;
        }

        private void EnsureSettingsExist()
        {
            if (!File.Exists(settingsFile))
            {
                string defaultSettings = "{\n" +
                    "  \"browser\": \"chrome\",\n" +
                    "  \"general_profile\": \"Default\",\n" +
                    "  \"music_profile\": \"Profile 1\",\n" +
                    "  \"general_url\": \"https://www.youtube.com\",\n" +
                    "  \"music_url\": \"resume\",\n" +
                    "  \"auto_hide_browser\": false,\n" +
                    "  \"isolate_music_browser\": true\n" +
                    "}";
                try
                {
                    File.WriteAllText(settingsFile, defaultSettings);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not create settings.json file: " + ex.Message, "YT Launcher Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void LoadSettings()
        {
            if (!File.Exists(settingsFile)) return;

            try
            {
                string json = File.ReadAllText(settingsFile);
                browser = GetJsonValue(json, "browser") ?? "chrome";
                generalProfile = GetJsonValue(json, "general_profile") ?? "Default";
                musicProfile = GetJsonValue(json, "music_profile") ?? "Profile 1";
                generalUrl = GetJsonValue(json, "general_url") ?? "https://www.youtube.com";
                musicUrl = GetJsonValue(json, "music_url") ?? "resume";
                
                string autoHideVal = GetJsonValue(json, "auto_hide_browser");
                autoHideBrowser = (autoHideVal != null && autoHideVal.Equals("true", StringComparison.OrdinalIgnoreCase));

                string isolateVal = GetJsonValue(json, "isolate_music_browser");
                if (isolateVal != null)
                {
                    isolateMusicBrowser = isolateVal.Equals("true", StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    isolateMusicBrowser = true; // Default to true if not present
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed reading settings.json: " + ex.Message, "YT Launcher Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SaveSettings()
        {
            try
            {
                string json = "{\n" +
                    "  \"browser\": \"" + browser + "\",\n" +
                    "  \"general_profile\": \"" + generalProfile + "\",\n" +
                    "  \"music_profile\": \"" + musicProfile + "\",\n" +
                    "  \"general_url\": \"" + generalUrl + "\",\n" +
                    "  \"music_url\": \"" + musicUrl + "\",\n" +
                    "  \"auto_hide_browser\": " + autoHideBrowser.ToString().ToLower() + ",\n" +
                    "  \"isolate_music_browser\": " + isolateMusicBrowser.ToString().ToLower() + "\n" +
                    "}";
                File.WriteAllText(settingsFile, json);
            }
            catch { }
        }

        private string GetJsonValue(string json, string key)
        {
            // Regex matches "key": "value" (with spaces inside quotes allowed) or "key": value (unquoted)
            string pattern = "\"" + key + "\"[\\s]*:[\\s]*(?:\"([^\"]*)\"|([^\",\\s}]+))";
            Match match = Regex.Match(json, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Success ? match.Groups[1].Value.Trim() : match.Groups[2].Value.Trim();
            }
            return null;
        }

        private void LaunchMusic(bool forceVisible = false)
        {
            LoadSettings(); // Hot-reload settings
            LaunchBrowser(browser, musicProfile, musicUrl);

            // Auto-hide window if enabled AND we are not forcing it to be visible
            if (autoHideBrowser && !forceVisible)
            {
                // Run in a background thread to wait for browser window, then hide it
                ThreadPool.QueueUserWorkItem((state) =>
                {
                    Thread.Sleep(4000); // Give browser 4 seconds to spawn window
                    musicWindowHandle = IntPtr.Zero;
                    EnumWindows(new EnumWindowsProc(FindMusicWindow), IntPtr.Zero);
                    if (musicWindowHandle != IntPtr.Zero)
                    {
                        ShowWindow(musicWindowHandle, SW_HIDE);
                        OptimizeMemory();
                        StartMemoryOptimizer();
                    }
                });
            }
        }

        private void LaunchGeneral()
        {
            LoadSettings(); // Hot-reload settings
            LaunchBrowser(browser, generalProfile, generalUrl);
        }

        private void EditSettings()
        {
            try
            {
                Process.Start("notepad.exe", settingsFile);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not open settings file in Notepad: " + ex.Message, "YT Launcher Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LaunchBrowser(string browserName, string profileName, string url)
        {
            string resolvedPath = ResolveBrowserPath(browserName);
            
            string args;
            if (isolateMusicBrowser && profileName == musicProfile)
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string userDataPath = Path.Combine(baseDir, "music_user_data");
                string extPath = Path.Combine(baseDir, "extension");
                
                if (string.IsNullOrEmpty(url) || url.Equals("resume", StringComparison.OrdinalIgnoreCase))
                {
                    args = string.Format("--user-data-dir=\"{0}\" --load-extension=\"{1}\" --profile-directory=\"Default\" --no-first-run --no-default-browser-check", userDataPath, extPath);
                }
                else
                {
                    args = string.Format("--user-data-dir=\"{0}\" --load-extension=\"{1}\" --profile-directory=\"Default\" --no-first-run --no-default-browser-check \"{2}\"", userDataPath, extPath, url);
                }
            }
            else
            {
                if (string.IsNullOrEmpty(url) || url.Equals("resume", StringComparison.OrdinalIgnoreCase))
                {
                    args = string.Format("--profile-directory=\"{0}\"", profileName);
                }
                else
                {
                    args = string.Format("--profile-directory=\"{0}\" \"{1}\"", profileName, url);
                }
            }
            
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = resolvedPath,
                    Arguments = args,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format("Failed to launch browser.\nTarget: {0}\nResolved Path: {1}\nArguments: {2}\n\nError: {3}\n\nOpening settings.json so you can correct your browser name.", browserName, resolvedPath, args, ex.Message),
                    "YT Launcher Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                EditSettings();
            }
        }

        private string ResolveBrowserPath(string browserName)
        {
            if (File.Exists(browserName))
                return browserName;

            string exeName = browserName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? browserName : browserName + ".exe";

            // Search Windows App Paths registries (Common location for registered executables)
            string[] regPaths = {
                @"Software\Microsoft\Windows\CurrentVersion\App Paths\" + exeName,
                @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\App Paths\" + exeName
            };

            foreach (var path in regPaths)
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(path))
                    {
                        if (key != null)
                        {
                            string val = key.GetValue("") as string;
                            if (!string.IsNullOrEmpty(val) && File.Exists(val))
                                return val;
                        }
                    }
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(path))
                    {
                        if (key != null)
                        {
                            string val = key.GetValue("") as string;
                            if (!string.IsNullOrEmpty(val) && File.Exists(val))
                                return val;
                        }
                    }
                }
                catch { }
            }

            // Standard hardcoded installation directories fallback
            if (exeName.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase))
            {
                string path1 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe");
                if (File.Exists(path1)) return path1;
                
                string path2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Google\Chrome\Application\chrome.exe");
                if (File.Exists(path2)) return path2;
                
                string path3 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe");
                if (File.Exists(path3)) return path3;
            }
            else if (exeName.Equals("msedge.exe", StringComparison.OrdinalIgnoreCase))
            {
                string path1 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe");
                if (File.Exists(path1)) return path1;
                
                string path2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe");
                if (File.Exists(path2)) return path2;
            }

            return exeName; // Return raw value if not found, letting ShellExecute find it
        }

        private static bool FindMusicWindow(IntPtr hWnd, IntPtr lParam)
        {
            StringBuilder title = new StringBuilder(256);
            GetWindowText(hWnd, title, 256);
            string titleStr = title.ToString();

            StringBuilder className = new StringBuilder(256);
            GetClassName(hWnd, className, 256);
            string classStr = className.ToString();

            // Match browser windows containing the unique "- YTMusicLauncher" suffix
            // Class names: Chrome_WidgetWin_1 (Chrome/Edge/Opera/Brave), MozillaWindowClass (Firefox)
            if ((classStr.Contains("Chrome_WidgetWin_1") || classStr.Contains("BraveWidgetWin_1") || classStr.Contains("MozillaWindowClass")) && 
                titleStr.Contains("- YTMusicLauncher"))
            {
                musicWindowHandle = hWnd;
                return false; // Stop enumerating
            }
            return true; // Continue enumerating
        }

        private void ToggleBrowserVisibility()
        {
            musicWindowHandle = IntPtr.Zero;
            EnumWindows(new EnumWindowsProc(FindMusicWindow), IntPtr.Zero);

            if (musicWindowHandle != IntPtr.Zero)
            {
                if (IsWindowVisible(musicWindowHandle))
                {
                    ShowWindow(musicWindowHandle, SW_HIDE);
                    StopFocusTimer();
                    OptimizeMemory();
                    StartMemoryOptimizer();
                }
                else
                {
                    ShowWindow(musicWindowHandle, SW_SHOW);
                    ShowWindow(musicWindowHandle, SW_RESTORE);
                    SetForegroundWindow(musicWindowHandle);
                    StartFocusTimer(); // Monitor focus since window is now shown!
                    StopMemoryOptimizer();
                }
            }
            else
            {
                // Browser is closed: automatically start it and keep it visible
                LaunchMusic(true);
                StartFocusMonitoring(); // Monitor focus since window is being spawned visible!
            }
        }

        private void TogglePlayPause()
        {
            musicWindowHandle = IntPtr.Zero;
            EnumWindows(new EnumWindowsProc(FindMusicWindow), IntPtr.Zero);

            if (musicWindowHandle != IntPtr.Zero)
            {
                // Send targeted WM_APPCOMMAND message to toggle YouTube play/pause (works even when browser is completely hidden!)
                IntPtr lParam = (IntPtr)(APPCOMMAND_MEDIA_PLAY_PAUSE << 16);
                SendMessage(musicWindowHandle, WM_APPCOMMAND, musicWindowHandle, lParam);
            }
            else
            {
                // Browser is closed: automatically start it in the background (hidden) and resume play!
                LoadSettings();
                
                // If musicUrl has a saved timestamp, launch that specific video, otherwise launch normally.
                LaunchBrowser(browser, musicProfile, musicUrl);

                // Run background polling task to capture and hide the window as soon as it spawns
                ThreadPool.QueueUserWorkItem((state) =>
                {
                    for (int i = 0; i < 40; i++) // Poll every 100ms for 4 seconds
                    {
                        Thread.Sleep(100);
                        musicWindowHandle = IntPtr.Zero;
                        EnumWindows(new EnumWindowsProc(FindMusicWindow), IntPtr.Zero);
                        
                        if (musicWindowHandle != IntPtr.Zero)
                        {
                            // Hide the window immediately
                            ShowWindow(musicWindowHandle, SW_HIDE);
                            OptimizeMemory();
                            StartMemoryOptimizer();
                            
                            // Give Chrome 500ms to load page layouts and hook media play bindings, then trigger play!
                            Thread.Sleep(500);
                            IntPtr lParam = (IntPtr)(APPCOMMAND_MEDIA_PLAY_PAUSE << 16);
                            SendMessage(musicWindowHandle, WM_APPCOMMAND, musicWindowHandle, lParam);
                            break;
                        }
                    }
                });
            }
        }

        private void HandleHotkey(int id)
        {
            switch (id)
            {
                case HOTKEY_PLAY_PAUSE:
                    TogglePlayPause();
                    break;
                case HOTKEY_SHOW_HIDE:
                    ToggleBrowserVisibility();
                    break;
            }
        }

        // --- SMART AUTO-HIDE FOCUS TIMER METHODS ---
        private void StartFocusTimer()
        {
            focusTimer.Change(0, 250); // Tick immediately, then every 250ms
        }

        private void StopFocusTimer()
        {
            focusTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        private void StartFocusMonitoring()
        {
            ThreadPool.QueueUserWorkItem((state) =>
            {
                // Wait up to 5 seconds for the window to appear and become visible
                for (int i = 0; i < 50; i++)
                {
                    Thread.Sleep(100);
                    musicWindowHandle = IntPtr.Zero;
                    EnumWindows(new EnumWindowsProc(FindMusicWindow), IntPtr.Zero);
                    if (musicWindowHandle != IntPtr.Zero && IsWindowVisible(musicWindowHandle))
                    {
                        StartFocusTimer();
                        break;
                    }
                }
            });
        }

        private void FocusTimerCallback(object state)
        {
            if (musicWindowHandle != IntPtr.Zero && IsWindowVisible(musicWindowHandle))
            {
                IntPtr foreground = GetForegroundWindow();
                if (foreground != IntPtr.Zero && foreground != musicWindowHandle)
                {
                    uint foregroundProcId;
                    GetWindowThreadProcessId(foreground, out foregroundProcId);
                    
                    uint musicProcId;
                    GetWindowThreadProcessId(musicWindowHandle, out musicProcId);
                    
                    // User switched away to a different application process
                    if (foregroundProcId != musicProcId)
                    {
                        StringBuilder className = new StringBuilder(256);
                        GetClassName(foreground, className, 256);
                        string classStr = className.ToString();
                        
                        // Ignore Windows Context menus (class "#32768") so clicking tray items doesn't trigger hide
                        if (classStr != "#32768")
                        {
                            ShowWindow(musicWindowHandle, SW_HIDE);
                            StopFocusTimer();
                            OptimizeMemory();
                            StartMemoryOptimizer();
                        }
                    }
                }
            }
            else
            {
                StopFocusTimer();
            }
        }

        private void CloseBrowserWindows()
        {
            // Find and close browser windows displaying YouTube Music profile
            EnumWindows(new EnumWindowsProc((hWnd, lParam) =>
            {
                StringBuilder title = new StringBuilder(256);
                GetWindowText(hWnd, title, 256);
                string titleStr = title.ToString();

                StringBuilder className = new StringBuilder(256);
                GetClassName(hWnd, className, 256);
                string classStr = className.ToString();

                if ((classStr.Contains("Chrome_WidgetWin_1") || classStr.Contains("BraveWidgetWin_1") || classStr.Contains("MozillaWindowClass")) && 
                    titleStr.Contains("- YTMusicLauncher"))
                {
                    // Post WM_CLOSE to gracefully close the window, allowing browser tab recovery to trigger
                    PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                }
                return true; // Keep searching for other YouTube windows
            }), IntPtr.Zero);
        }

        // --- HTTP SERVER FOR CHROME EXTENSION ---
        private void StartHttpServer()
        {
            try
            {
                listener = new HttpListener();
                listener.Prefixes.Add("http://localhost:18293/");
                listener.Start();

                listenerThread = new Thread(ListenLoop);
                listenerThread.IsBackground = true;
                listenerThread.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("HTTP Server failed to start: " + ex.Message);
            }
        }

        private void ListenLoop()
        {
            while (listener != null && listener.IsListening)
            {
                try
                {
                    HttpListenerContext context = listener.GetContext();
                    HttpListenerRequest request = context.Request;
                    HttpListenerResponse response = context.Response;

                    // Handle CORS Preflight
                    if (request.HttpMethod == "OPTIONS")
                    {
                        response.AddHeader("Access-Control-Allow-Origin", "*");
                        response.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                        response.AddHeader("Access-Control-Allow-Headers", "Content-Type");
                        response.StatusCode = (int)HttpStatusCode.OK;
                        response.Close();
                        continue;
                    }

                    string responseString = "OK";
                    string contentType = "text/plain";

                    if (request.Url.AbsolutePath.Equals("/update", StringComparison.OrdinalIgnoreCase))
                    {
                        string rawUrl = request.QueryString["url"];
                        string timeStr = request.QueryString["time"];

                        if (!string.IsNullOrEmpty(rawUrl) && !string.IsNullOrEmpty(timeStr))
                        {
                            UpdateLastWatched(rawUrl, timeStr);
                        }
                    }
                    else if (request.Url.AbsolutePath.Equals("/status", StringComparison.OrdinalIgnoreCase))
                    {
                        bool isHidden = true;
                        musicWindowHandle = IntPtr.Zero;
                        EnumWindows(new EnumWindowsProc(FindMusicWindow), IntPtr.Zero);
                        if (musicWindowHandle != IntPtr.Zero)
                        {
                            isHidden = !IsWindowVisible(musicWindowHandle);
                        }
                        responseString = "{\"hidden\":" + isHidden.ToString().ToLower() + "}";
                        contentType = "application/json";
                    }

                    response.AddHeader("Access-Control-Allow-Origin", "*");
                    response.ContentType = contentType;
                    byte[] buffer = Encoding.UTF8.GetBytes(responseString);
                    response.ContentLength64 = buffer.Length;
                    Stream output = response.OutputStream;
                    output.Write(buffer, 0, buffer.Length);
                    output.Close();
                }
                catch { }
            }
        }

        private void UpdateLastWatched(string url, string timeStr)
        {
            try
            {
                double seconds = 0;
                double.TryParse(timeStr, out seconds);
                int secs = (int)Math.Floor(seconds);

                // Strip existing timestamp query parameters to clean the url
                string cleanUrl = Regex.Replace(url, @"&t=\d+s?", "");
                cleanUrl = Regex.Replace(cleanUrl, @"\?t=\d+s?", "");

                // Append the new timestamp query parameter (e.g. &t=142)
                string separator = cleanUrl.Contains("?") ? "&" : "?";
                string timestampedUrl = cleanUrl + separator + "t=" + secs;

                // Only write if url has changed to avoid excessive file writes
                if (musicUrl != timestampedUrl)
                {
                    musicUrl = timestampedUrl;
                    SaveSettings();
                }
            }
            catch { }
        }

        private void FlushProcessesMemory()
        {
            musicWindowHandle = IntPtr.Zero;
            EnumWindows(new EnumWindowsProc(FindMusicWindow), IntPtr.Zero);
            if (musicWindowHandle == IntPtr.Zero) return;

            uint mainPid;
            GetWindowThreadProcessId(musicWindowHandle, out mainPid);
            if (mainPid == 0) return;

            // Build parent-to-children process map
            var parentToChildren = new System.Collections.Generic.Dictionary<uint, System.Collections.Generic.List<uint>>();
            IntPtr hSnapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (hSnapshot != IntPtr.Zero)
            {
                try
                {
                    PROCESSENTRY32 pe32 = new PROCESSENTRY32();
                    pe32.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32));
                    if (Process32First(hSnapshot, ref pe32))
                    {
                        do
                        {
                            uint pid = pe32.th32ProcessID;
                            uint parentPid = pe32.th32ParentProcessID;
                            if (!parentToChildren.ContainsKey(parentPid))
                            {
                                parentToChildren[parentPid] = new System.Collections.Generic.List<uint>();
                            }
                            parentToChildren[parentPid].Add(pid);
                        } while (Process32Next(hSnapshot, ref pe32));
                    }
                }
                catch { }
                finally
                {
                    CloseHandle(hSnapshot);
                }
            }

            // Collect main process and all descendants
            var pidsToFlush = new System.Collections.Generic.List<uint>();
            var queue = new System.Collections.Generic.Queue<uint>();
            queue.Enqueue(mainPid);
            while (queue.Count > 0)
            {
                uint current = queue.Dequeue();
                pidsToFlush.Add(current);
                if (parentToChildren.ContainsKey(current))
                {
                    foreach (uint child in parentToChildren[current])
                    {
                        queue.Enqueue(child);
                    }
                }
            }

            // Flush working set for each process in the tree
            foreach (uint pid in pidsToFlush)
            {
                IntPtr hProcess = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_SET_QUOTA, false, pid);
                if (hProcess != IntPtr.Zero)
                {
                    try
                    {
                        EmptyWorkingSet(hProcess);
                    }
                    catch { }
                    finally
                    {
                        CloseHandle(hProcess);
                    }
                }
            }
        }

        private void OptimizeMemory()
        {
            // Delay 1 second to let browser process finish window operations and stabilize
            ThreadPool.QueueUserWorkItem((state) =>
            {
                Thread.Sleep(1000);
                FlushProcessesMemory();
            });
        }

        private void StartMemoryOptimizer()
        {
            memoryOptimizerTimer.Change(10000, 15000); // Wait 10 seconds, then tick every 15 seconds
        }

        private void StopMemoryOptimizer()
        {
            memoryOptimizerTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        private void MemoryOptimizerCallback(object state)
        {
            musicWindowHandle = IntPtr.Zero;
            EnumWindows(new EnumWindowsProc(FindMusicWindow), IntPtr.Zero);
            if (musicWindowHandle != IntPtr.Zero && !IsWindowVisible(musicWindowHandle))
            {
                FlushProcessesMemory();
            }
        }


        private void StopHttpServer()
        {
            try
            {
                if (listener != null)
                {
                    listener.Stop();
                    listener.Close();
                    listener = null;
                }
                if (listenerThread != null)
                {
                    listenerThread.Join(500);
                    listenerThread = null;
                }
            }
            catch { }
        }

        private Icon CreateDynamicIcon()
        {
            try
            {
                using (Bitmap bmp = new Bitmap(32, 32))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.Clear(Color.Transparent);
                        
                        // Draw rounded container (dark crimson circle)
                        using (Brush bgBrush = new SolidBrush(Color.FromArgb(24, 24, 34)))
                        {
                            g.FillEllipse(bgBrush, 2, 2, 28, 28);
                        }
                        
                        // Draw a glowing outer border
                        using (Pen pen = new Pen(Color.FromArgb(255, 42, 84), 1.5f))
                        {
                            g.DrawEllipse(pen, 2, 2, 28, 28);
                        }
                        
                        // Draw play button triangle
                        PointF[] points = {
                            new PointF(13f, 10f),
                            new PointF(13f, 22f),
                            new PointF(22f, 16f)
                        };
                        using (Brush playBrush = new SolidBrush(Color.FromArgb(255, 42, 84)))
                        {
                            g.FillPolygon(playBrush, points);
                        }
                    }
                    return Icon.FromHandle(bmp.GetHicon());
                }
            }
            catch
            {
                return SystemIcons.Application;
            }
        }

        private void Exit()
        {
            // Unregister hotkeys
            if (hotkeyWindow != null)
            {
                UnregisterHotKey(hotkeyWindow.Handle, HOTKEY_PLAY_PAUSE);
                UnregisterHotKey(hotkeyWindow.Handle, HOTKEY_SHOW_HIDE);
                hotkeyWindow.Close();
                hotkeyWindow.Dispose();
            }
            
            // Stop Smart Auto-Hide Timer
            if (focusTimer != null)
            {
                focusTimer.Dispose();
            }
            
            if (memoryOptimizerTimer != null)
            {
                memoryOptimizerTimer.Dispose();
            }

            StopHttpServer();
            CloseBrowserWindows();
            Thread.Sleep(250); // Let processes begin closing gracefully before app exits
            trayIcon.Visible = false;
            trayIcon.Dispose();
            Application.Exit();
        }
    }
}
