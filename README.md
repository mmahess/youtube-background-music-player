# YT Launcher 🎧

**YT Launcher** is an ultra-lightweight Windows system tray utility and synchronization engine designed to isolate and optimize background YouTube music playback. 

It is designed for users who like to **use a specific standard YouTube account (or dedicated music channel) solely to play music in the background**, keeping their main browsing history, login state, and recommendations completely separate.

---

## Key Features

*   **Browser Process Isolation:** Automatically runs your YouTube Music instance in a dedicated, isolated process tree using a local user data directory. This keeps your logins, cookies, and search history separate from your daily browser windows.
*   **Zero-Overhead Background Playback:**
    *   **RAM Compression:** Uses native Windows `EmptyWorkingSet` APIs to continuously compress and page out inactive browser pages when hidden, maintaining a background RAM footprint of just **20MB - 50MB** (down from 700MB+).
    *   **CPU & GPU Suspension:** Automatically sets the YouTube `<video>` element to `visibility: hidden` when backgrounded, halting background GPU and CPU video decoding to near **0%** usage.
*   **Tray & Global Controls:**
    *   `Ctrl + Alt + H` (or tray click): Instant Show/Hide browser.
    *   `Ctrl + Alt + P`: Play/Pause music from anywhere, even when the browser is completely hidden.
*   **Smart Auto-Hide:** Hides the player browser window automatically as soon as you focus or click into another application window.
*   **Automated Extension Injection:** Automatically loads the synchronization extension on startup, removing the need for manual unpacked extension loading.
*   **Timestamp Sync:** Automatically saves your last watched video URL and timestamp so that clicking **Pause/Resume** resumes exactly where you left off.

---

## Recommended Setup (Important!) 🚀

To get the absolute best, uninterrupted background music experience, we highly recommend opening the isolated window once and installing the following extensions from the Chrome Web Store:

1.  **YouTube Adblocker (e.g. uBlock Origin):** Prevents ads from interrupting your music streams.
2.  **YouTube NonStop:** Prevents YouTube's annoying *"Video paused. Continue watching?"* confirmation prompt during long background sessions.

---

## How it Works

*   [YTLauncher.cs](YTLauncher.cs) runs a native, lightweight C# Windows Forms tray icon and hosts a background HTTP listener at `http://localhost:18293`.
*   [extension/content.js](extension/content.js) is a content script loaded into the isolated browser. It reports video elapsed time and URL changes to the listener, tags the window title, and pauses rendering based on window visibility status.

---

## Setup & Run

### 1. Rebuild (Optional)
If you make changes to the C# source, run the compile script:
```cmd
.\compile.bat
```

### 2. Startup
Run the compiled executable `YTLauncher.exe`. It will start in your system tray.

### 3. Log In
1. Right-click the tray icon and select **YouTube (Music Account)**.
2. An isolated browser window will open.
3. Log in to your YouTube Music account once. Your login will remain persisted in the isolated folder.

### 4. Enable Startup (Optional)
To make the tray launcher start automatically when you boot Windows:
1. Press `Win + R`, type `shell:startup`, and press Enter.
2. Right-click and create a shortcut pointing to your compiled `YTLauncher.exe` file inside this startup directory.
