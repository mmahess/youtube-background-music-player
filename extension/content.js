let lastTime = 0;
let lastUrl = '';
let isWindowHidden = false;

// Unique suffix to let the C# tray app identify the Music profile window
const suffix = ' - YTMusicLauncher';

function updateTitle() {
  if (window.location.host.includes('youtube.com') && !document.title.includes(suffix)) {
    document.title = document.title + suffix;
  }
}

function sendUpdate() {
  const video = document.querySelector('video');
  if (!video) return;

  const currentTime = video.currentTime;
  const currentUrl = window.location.href;

  if (!currentUrl.includes('/watch')) return;

  // Send update if URL changed or playback progressed
  // If hidden, only send if progression is >= 10 seconds to reduce network requests
  const threshold = isWindowHidden ? 10 : 3;
  if (currentUrl !== lastUrl || Math.abs(currentTime - lastTime) >= threshold) {
    lastUrl = currentUrl;
    lastTime = currentTime;

    fetch(`http://localhost:18293/update?url=${encodeURIComponent(currentUrl)}&time=${Math.floor(currentTime)}`)
      .catch(err => {
        // Silently catch if server isn't running
      });
  }
}

// Polling status of window visibility from the launcher
function checkWindowStatus() {
  fetch('http://localhost:18293/status')
    .then(res => res.json())
    .then(data => {
      const targetHidden = !!data.hidden;
      if (isWindowHidden !== targetHidden) {
        isWindowHidden = targetHidden;
        applyVideoVisibility();
      }
    })
    .catch(err => {
      // Silently catch if server is not reachable
    });
}

function applyVideoVisibility() {
  const video = document.querySelector('video');
  if (!video) return;

  if (isWindowHidden) {
    if (video.style.visibility !== 'hidden') {
      video.style.visibility = 'hidden';
      console.log('YT Launcher: Suspended video decoding.');
    }
  } else {
    if (video.style.visibility !== 'visible') {
      video.style.visibility = 'visible';
      console.log('YT Launcher: Restored video decoding.');
    }
  }
}

// Set up event listener for tab visibility change
document.addEventListener('visibilitychange', () => {
  if (document.hidden) {
    isWindowHidden = true;
  } else {
    isWindowHidden = false;
  }
  applyVideoVisibility();
  checkWindowStatus(); // Immediate poll on transition
});

// Unified scheduler loop tick count
let tickCount = 0;
function masterLoop() {
  tickCount++;

  // Title update:
  // - Visible: every 1 second
  // - Hidden: every 10 seconds
  const titleInterval = isWindowHidden ? 10 : 1;
  if (tickCount % titleInterval === 0) {
    updateTitle();
  }

  // Playback timestamp update:
  // - Visible: every 2 seconds
  // - Hidden: every 10 seconds
  const updateInterval = isWindowHidden ? 10 : 2;
  if (tickCount % updateInterval === 0) {
    sendUpdate();
  }

  // Status check:
  // - Visible: every 2 seconds
  // - Hidden: every 3 seconds
  const statusInterval = isWindowHidden ? 3 : 2;
  if (tickCount % statusInterval === 0) {
    checkWindowStatus();
  }

  // Verify visibility style in case player refreshed elements
  // - Visible: every 2 seconds
  // - Hidden: every 5 seconds
  const visInterval = isWindowHidden ? 5 : 2;
  if (tickCount % visInterval === 0) {
    applyVideoVisibility();
  }

  // Reset tick count to prevent overflow
  if (tickCount >= 30) {
    tickCount = 0;
  }
}

// Run master loop every 1 second
setInterval(masterLoop, 1000);

console.log('YT Launcher Sync: Unified scheduling loop active.');
