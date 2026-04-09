# Headless Screenshots for WinForms MCP

## 1. Problem Statement

The Rhombus.WinFormsMcp server needs to capture screenshots of WinForms applications without disrupting the user's workflow. There are two primary deployment environments:

1. **User's desktop (primary)** -- A developer is actively working (typing in their IDE, browsing, etc.) and the MCP server launches/automates WinForms apps in the background. The MCP must NEVER steal focus, flash windows, or otherwise interrupt the user.

2. **CI environment (secondary)** -- GitHub Actions `windows-latest` runners, which have an interactive desktop session but no human user. Reliability matters here, but focus stealing is not a concern.

There are two distinct screenshot scenarios:

1. **Form rendering from source code** (`render_form`) -- Already works headlessly because it uses `DrawToBitmap()` via DesignSurface, which renders to an off-screen buffer without requiring a visible window or display.

2. **Live application screenshot** (`take_screenshot` tool via `AutomationHelper.TakeScreenshot`) -- This uses FlaUI's `element.Capture()` / `desktop.Capture()`, which relies on `System.Drawing.Graphics.CopyFromScreen()` under the hood. This requires:
   - A visible, on-screen window with non-zero bounding rectangle
   - An active desktop session (GDI+ screen capture)
   - The element must not be minimized or have `IntPtr.Zero` as its window handle

The current `_headless` flag sets `CreateNoWindow = true` and `WindowStyle = ProcessWindowStyle.Hidden`, which causes `MainWindowHandle` to be `IntPtr.Zero`. FlaUI's `_automation.FromHandle(IntPtr.Zero)` fails, and even if it didn't, `Capture.Element()` would have nothing to capture since the window is never rendered to a display surface.

---

## 2. Research Findings

### Approach 1: Virtual Desktop / Virtual Display on Windows

**Concept:** On Linux, Xvfb provides a virtual framebuffer. Is there a Windows equivalent?

**Findings:**
- **There is no direct Windows equivalent of Xvfb.** Windows does not have a virtual framebuffer driver built in.
- **Windows Virtual Desktop API** (`IVirtualDesktopManager` from `Microsoft.Windows.SDK.Contracts`) creates additional virtual desktops within an existing session. It does NOT create a virtual display -- it still requires a real desktop session. Irrelevant for headless scenarios.
- **Indirect Display Driver (IddCx)**: Windows 10 1903+ supports Indirect Display Drivers that create virtual monitors. Microsoft's `IddSampleDriver` on GitHub demonstrates this. However:
  - Requires a kernel-mode driver to be installed (admin + test signing or WHQL)
  - Not practical for CI
  - Licensing: MIT (Microsoft sample), but driver signing is the blocker
- **Third-party tools:**
  - **Virtual Display Driver** (github.com/itsmikethetech/Virtual-Display-Driver) -- open source IddCx driver, creates virtual monitors. Requires driver installation (admin, test signing). Not CI-friendly.
  - **parsec-vdd / usbmmidd** -- same category, same limitations.
  - **Windows Sandbox / Hyper-V** -- could theoretically provide an isolated desktop, but extreme overhead for a screenshot.

**Verdict:** No practical virtual display solution exists for Windows CI. Dead end for the `take_screenshot` use case.

| Criterion | Assessment |
|---|---|
| Works on user desktop | No (requires driver install) |
| Works in CI | No (requires driver install) |
| Works with FlaUI | Yes (if display exists) |
| Admin required | Yes (kernel driver) |
| Steals focus | N/A |
| Licensing | Varies, mostly MIT |

---

### Approach 2: Windows Session 0 / Service Session Isolation

**Concept:** Run the WinForms app in Session 0 (the services session) and capture screenshots there.

**Findings:**
- Since Windows Vista/Server 2008, Session 0 is isolated. It has a non-interactive desktop.
- WinForms apps CAN run in Session 0 and create windows, but they are not visible to any interactive desktop.
- `Graphics.CopyFromScreen()` in Session 0 captures a blank/black screen because there is no compositor rendering to Session 0's desktop.
- UI Automation (UIA) works across sessions to some extent, but `Capture()` still needs GDI+ screen capture which requires rendered pixels.
- **GitHub Actions `windows-latest` runners already run in an interactive session** (see Approach 5), so Session 0 is not how CI runners work.

**Verdict:** Session 0 makes things worse, not better. The window exists but is never rendered, so there is nothing to capture.

| Criterion | Assessment |
|---|---|
| Works on user desktop | No (service installation) |
| Works in CI | No |
| Works with FlaUI | Partially (element discovery yes, capture no) |
| Admin required | Yes (service installation) |
| Steals focus | No (different session) |
| Licensing | N/A |

---

### Approach 3: Off-Screen Window Positioning

**Concept:** Launch the app normally (not hidden), but position it at (-32000, -32000) so it is technically "shown" but off-screen. This is exactly what the form renderers already do.

**Findings:**
- When a window is "shown" (via `Show()` or normal launch with `WindowStyle.Normal`), Windows creates the window, assigns an HWND, and renders it -- even if positioned off-screen.
- `DrawToBitmap()` works perfectly with off-screen windows because it uses `WM_PRINT` / `WM_PRINTCLIENT` messages that render to a provided DC rather than the screen.
- **FlaUI's `element.Capture()`** uses `Graphics.CopyFromScreen()` which copies from the actual screen buffer. For off-screen windows, the pixels are NOT in the screen buffer, so `CopyFromScreen` captures garbage or black.
- **However**, `element.BoundingRectangle` is still valid for off-screen windows (it will report the off-screen coordinates). FlaUI element discovery and UIA property access work fine.
- The `render_form` tool (DesignSurfaceFormRenderer) already uses this approach with `DrawToBitmap`, proving it works for rendering.

**Key insight:** Off-screen positioning + `DrawToBitmap` works, but only for forms you control. For the `take_screenshot` tool that captures arbitrary running applications, you can't call `DrawToBitmap` on an external process's window.

**Alternative for external windows:** Use `PrintWindow` Win32 API (see Approach 4).

| Criterion | Assessment |
|---|---|
| Works on user desktop | Yes (for DrawToBitmap path) |
| Works in CI | Yes (for DrawToBitmap path) |
| Works with FlaUI | Element discovery: yes. Capture(): no. |
| Admin required | No |
| Steals focus | Partially -- see Focus Stealing Analysis below |
| Licensing | N/A |

---

### Approach 4: `PrintWindow` Win32 API

**Concept:** `User32.PrintWindow(hWnd, hDC, flags)` sends `WM_PRINT` to a window and captures its contents to a device context you provide. Unlike `CopyFromScreen`, it works for:
- Off-screen windows
- Occluded (covered) windows
- Windows on other virtual desktops
- Windows in another position

**Findings:**
- `PrintWindow` with `PW_RENDERFULLCONTENT` flag (value 0x00000002, available since Windows 8.1) captures DWM-composed content including DirectX surfaces.
- `PrintWindow` with flag 0 (default) sends `WM_PRINT` to the window, which works for GDI-rendered content (WinForms is GDI-based, so this is perfect).
- **Minimized windows:** `PrintWindow` does NOT work for minimized windows (they have zero-size client area). The window must be in a "shown" state, even if off-screen. `SW_SHOWNOACTIVATE` or positioning off-screen is needed.
- **Hidden windows** (`ShowWindow(SW_HIDE)` / `CreateNoWindow = true`): Does NOT work -- the window has no HWND or a special hidden HWND.
- **Works in CI:** Yes, as long as the window has an HWND and is in a "shown" state. The key requirement is that the process was started with a visible window style (even if positioned off-screen).

**P/Invoke signature:**
```csharp
[DllImport("user32.dll")]
static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

// Usage:
var rect = new RECT();
GetWindowRect(hWnd, out rect);
var width = rect.Right - rect.Left;
var height = rect.Bottom - rect.Top;
using var bmp = new Bitmap(width, height);
using var gfx = Graphics.FromImage(bmp);
var hdc = gfx.GetHdc();
PrintWindow(hWnd, hdc, 0); // or PW_RENDERFULLCONTENT = 2
gfx.ReleaseHdc(hdc);
```

**This is the most promising approach for capturing external application windows headlessly.**

| Criterion | Assessment |
|---|---|
| Works on user desktop | Yes (window must be shown, not minimized/hidden) |
| Works in CI | Yes (same requirement) |
| Works with FlaUI | Complementary -- use FlaUI for element discovery, PrintWindow for capture |
| Admin required | No |
| Steals focus | No (PrintWindow itself does not affect focus) |
| Licensing | Win32 API, no licensing concerns |

---

### Approach 5: GitHub Actions `windows-latest` Runner Specifics

**Findings:**
- **GitHub Actions `windows-latest` (currently Windows Server 2022) runners DO have an interactive desktop session.** The runner agent runs in an interactive logon session, not as a service.
- The desktop is 1024x768 or similar default resolution.
- There is no physical monitor, but Windows provides a virtual desktop surface for the interactive session.
- **FlaUI works out of the box on GitHub Actions windows runners** for apps that are launched normally (not hidden). Multiple open-source projects use FlaUI in GitHub Actions CI.
- `Graphics.CopyFromScreen()` works because there IS a desktop surface to copy from.
- The critical issue is the current code's `CreateNoWindow = true` / `WindowStyle.Hidden` which prevents the window from being created visibly.

**This is a crucial finding:** The headless flag's current implementation (hiding the window) is actually counterproductive on GitHub Actions. The window should be SHOWN but positioned off-screen, which is enough for both FlaUI element discovery AND screenshot capture.

**Desktop resolution matters:** `CopyFromScreen` can only capture what's within the desktop bounds. Off-screen windows (at -32000, -32000) are outside these bounds. So either:
1. Position the window at (0, 0) or within bounds (FlaUI capture works, window is "visible" in the session)
2. Use `PrintWindow` which doesn't care about screen bounds

| Criterion | Assessment |
|---|---|
| Works on user desktop | N/A (CI-specific finding) |
| Works in CI | Yes, windows-latest has interactive desktop |
| Works with FlaUI | Yes, if window is shown (not hidden) |
| Admin required | No |
| Steals focus | N/A (no human user) |
| Licensing | N/A |

---

### Approach 6: Headless Form Rendering Without Launching the App

**Concept:** Extend the DesignSurfaceFormRenderer approach to handle the `take_screenshot` use case. Instead of launching an external app, render the form via DesignSurface and use `DrawToBitmap`.

**Findings:**
- The `render_form` tool already does exactly this for form preview. It parses `.Designer.cs` code via Roslyn, instantiates controls on a DesignSurface, and calls `DrawToBitmap`.
- **However**, this only works for static form previews. The `take_screenshot` tool is meant to capture a RUNNING application's current state -- with data loaded, user interactions applied, dynamic content, etc.
- You cannot use `DrawToBitmap` on an external process's form from another process.
- This approach is fundamentally different from live screenshot capture.

**Verdict:** Already implemented for its intended purpose (form preview). Not applicable to live app screenshots.

| Criterion | Assessment |
|---|---|
| Works on user desktop | Yes |
| Works in CI | Yes |
| Works with FlaUI | N/A (bypasses FlaUI) |
| Admin required | No |
| Steals focus | No |
| Licensing | N/A |

---

### Approach 7: `User32.dll` MoveWindow/ShowWindow Tricks

**Concept:** When the headless flag is set, instead of hiding the window, launch it normally, then immediately move it off-screen or to a non-disruptive position. Use `ShowWindow(SW_SHOWNOACTIVATE)` to avoid stealing focus.

**Findings:**
- `ShowWindow(hWnd, SW_SHOWNOACTIVATE)` shows the window without activating it or bringing it to front.
- `MoveWindow(hWnd, -32000, -32000, width, height, false)` positions it off-screen.
- `SetWindowPos(hWnd, HWND_BOTTOM, ...)` keeps it behind all other windows.
- Combined with `PrintWindow` (Approach 4), this gives full headless screenshot capability.
- On GitHub Actions runners (which have a desktop), you could even position the window at (0, 0) and use FlaUI's native `Capture()` without `PrintWindow`.

**The workflow for headless mode would be:**
1. Launch app normally (no `CreateNoWindow`, no `WindowStyle.Hidden`)
2. Wait for `MainWindowHandle` to be non-zero
3. Move window off-screen via `MoveWindow` or `SetWindowPos`
4. For screenshots, use `PrintWindow` to capture regardless of position
5. FlaUI element discovery works because the window has a real HWND and is in the automation tree

| Criterion | Assessment |
|---|---|
| Works on user desktop | Yes |
| Works in CI | Yes |
| Works with FlaUI | Yes (real HWND = full UIA tree) |
| Admin required | No |
| Steals focus | Partially -- see Focus Stealing Analysis below |
| Licensing | Win32 API, no concerns |

---

### Approach 8: Docker Windows Containers

**Concept:** Run the WinForms app inside a Windows Docker container to get complete isolation from the user's desktop. No focus stealing possible because the container has its own session.

**Findings:**

- **Base image:** `mcr.microsoft.com/windows/servercore` is the recommended base for WinForms/WPF apps. It provides high Win32 API compatibility with a reasonable image size (~5GB compressed).
- **No GUI session in containers:** Windows containers with process isolation only provide access to a service console session (analogous to Session 0). There is NO interactive desktop inside a standard Windows container.
- **WinForms can start but cannot render:** A WinForms app can be installed and launched inside a container, but since there is no display session, windows are created in a non-interactive window station. `Graphics.CopyFromScreen()` returns black. `PrintWindow()` also returns black because it requires `WinSta0` (the interactive window station) for I/O.
- **RDP workaround:** Some guides suggest enabling Remote Desktop Services inside the container and connecting via RDP. This creates an interactive session, but:
  - Adds massive complexity (RDP server setup, authentication, connection management)
  - Defeats the purpose of lightweight automation
  - Significant latency overhead for each screenshot
- **windows-container-view:** A proof-of-concept project (github.com/smallmodel/windows-container-view) demonstrates retrieving visual information from a headless container, but it is experimental and not production-ready.
- **Feature gap:** The Windows Containers team has open feature requests for GUI support (microsoft/Windows-Containers#306, #611, #27). As of 2026, this is still not natively supported.
- **Performance:** Container startup time alone (~2-5 seconds for a warm container, 10-30+ seconds for a cold start) far exceeds the ~450ms render target. Plus image pull time on first use.
- **Docker Desktop requirement:** Windows containers require either Docker Desktop (which itself uses Hyper-V) or the Docker Engine on Windows Server. This is a heavy dependency for an MCP tool.

**Verdict:** Docker Windows containers do NOT have a display session. WinForms apps cannot render visible windows inside them, making screenshot capture impossible without extreme workarounds (RDP). The performance overhead and complexity make this impractical.

| Criterion | Assessment |
|---|---|
| Works on user desktop | Yes (complete isolation) |
| Works in CI | Difficult (nested containers, image size) |
| Works with FlaUI | No (no interactive desktop in container) |
| PrintWindow works | No (non-interactive window station) |
| Admin required | No (but Docker Desktop required) |
| Steals focus | No (complete isolation) |
| Performance | Very poor (~5-30s startup vs 450ms target) |
| Complexity | Very high |
| Licensing | Docker Desktop: free for small business, paid for enterprise |

---

### Approach 9: `CreateDesktop` Win32 API (Hidden Desktop)

**Concept:** Use the Win32 `CreateDesktop` API to create a separate, invisible desktop within the same window station. Launch the WinForms app on that desktop. The app runs in complete UI isolation from the user's visible desktop -- it cannot steal focus because it exists on a different desktop entirely.

**Findings:**

- **How it works:** Windows supports multiple desktops within a single window station. Only one desktop is "active" (displayed on the monitor) at a time. `CreateDesktop` creates a new desktop that is NOT active/visible. Processes launched with `STARTUPINFO.lpDesktop` set to the new desktop's name will create all their windows on that hidden desktop.
- **Window isolation is complete:** Windows on a hidden desktop are entirely separate from the user's visible desktop. They cannot steal focus, appear in the taskbar, or interact with the user's windows in any way. Even `SetForegroundWindow`, `this.Activate()`, `TopMost = true`, `textBox1.Focus()` -- all of these operate within the hidden desktop's scope and have zero effect on the user's active desktop.
- **PrintWindow works:** `PrintWindow` can capture windows on a hidden desktop. The technique (used by Hidden VNC implementations) is:
  1. `EnumDesktopWindows` to find windows on the hidden desktop
  2. `PrintWindow` on each window to capture its content
  3. For WinForms (GDI-based), `PrintWindow` with flag 0 sends `WM_PRINT` which works reliably
- **Rendering caveat:** Windows does NOT run the DWM compositor for non-active desktops. This means:
  - GDI-rendered content (WinForms) works via `PrintWindow` because `WM_PRINT` triggers client-side rendering
  - DirectX/WPF content may not render correctly (not a concern for WinForms)
  - `Graphics.CopyFromScreen()` on a hidden desktop captures black (no compositor output)
- **FlaUI compatibility:** UI Automation (UIA) can discover elements across desktops within the same window station. However, the automation thread may need to call `SetThreadDesktop` to the hidden desktop to fully enumerate elements. This needs testing.
- **Implementation in C#:** Requires P/Invoke for `CreateDesktop`, `SetThreadDesktop`, and `CreateProcess` (since `System.Diagnostics.Process.Start` does not expose `STARTUPINFO.lpDesktop`). Moderate complexity.

**P/Invoke signatures:**
```csharp
[DllImport("user32.dll", SetLastError = true)]
static extern IntPtr CreateDesktop(string lpszDesktop, IntPtr lpszDevice,
    IntPtr pDevmode, uint dwFlags, uint dwDesiredAccess, IntPtr lpsa);

[DllImport("user32.dll", SetLastError = true)]
static extern bool SetThreadDesktop(IntPtr hDesktop);

[DllImport("user32.dll", SetLastError = true)]
static extern bool CloseDesktop(IntPtr hDesktop);

[DllImport("user32.dll")]
static extern bool EnumDesktopWindows(IntPtr hDesktop, EnumWindowsProc lpfn, IntPtr lParam);

// Launch process on hidden desktop:
// STARTUPINFO.lpDesktop = "WinSta0\\MyHiddenDesktop";
// CreateProcess(..., ref si, ...);
```

**This approach provides the strongest isolation guarantee.** It is the only approach where `this.Activate()`, `TopMost = true`, and `textBox1.Focus()` inside the form under test are guaranteed to have ZERO impact on the user.

| Criterion | Assessment |
|---|---|
| Works on user desktop | Yes (complete UI isolation) |
| Works in CI | Yes (same API available) |
| Works with FlaUI | Needs testing (UIA cross-desktop) |
| PrintWindow works | Yes (individual window capture) |
| Admin required | No |
| Steals focus | Impossible (different desktop) |
| Performance | Minimal overhead (desktop creation is fast) |
| Complexity | Medium-high (P/Invoke for CreateProcess, desktop management) |
| Licensing | Win32 API, no concerns |

---

### Approach 10: `CreateWindowStation` + `CreateDesktop` (Full Window Station Isolation)

**Concept:** Go one step further than Approach 9 -- create a separate window station AND desktop. This provides even deeper isolation (separate clipboard, atom table, etc.).

**Findings:**
- A window station contains one or more desktops, a clipboard, and an atom table.
- `CreateWindowStation` creates a new window station within the current session.
- Processes on a different window station cannot interact with the user's desktop at all.
- **However**, `PrintWindow` only works for windows in a window station that allows I/O, which by default is only `WinSta0`. Windows on a non-interactive window station produce black output from `PrintWindow`.
- This means creating a new window station would PREVENT `PrintWindow` from working, defeating the purpose.

**Verdict:** `CreateWindowStation` goes too far -- it breaks `PrintWindow`. Use `CreateDesktop` within `WinSta0` instead (Approach 9).

| Criterion | Assessment |
|---|---|
| Works on user desktop | Yes (isolation) |
| Works in CI | Yes |
| PrintWindow works | **No** (non-interactive window station) |
| Admin required | No |
| Steals focus | Impossible |
| Licensing | Win32 API, no concerns |

---

### Approach 11: Windows Job Objects

**Concept:** Use Windows Job Objects to contain the launched WinForms process, restricting its ability to interact with the user's UI.

**Findings:**
- Job Objects group processes and manage them as a unit. They can enforce resource limits (CPU, memory, I/O) and UI restrictions.
- **UI restrictions available via `JOBOBJECT_BASIC_UI_RESTRICTIONS`:**
  - `JOB_OBJECT_UILIMIT_DESKTOP` -- prevents processes from switching or creating desktops
  - `JOB_OBJECT_UILIMIT_DISPLAYSETTINGS` -- prevents changing display settings
  - `JOB_OBJECT_UILIMIT_GLOBALATOMS` -- restricts global atom access
  - `JOB_OBJECT_UILIMIT_HANDLES` -- prevents accessing handles owned by threads outside the job (this partially limits focus stealing)
  - `JOB_OBJECT_UILIMIT_READCLIPBOARD` / `JOB_OBJECT_UILIMIT_WRITECLIPBOARD` -- clipboard restrictions
  - `JOB_OBJECT_UILIMIT_SYSTEMPARAMETERS` -- prevents changing system parameters
- **What Job Objects CANNOT do:**
  - They cannot prevent `SetForegroundWindow` calls from within the job
  - They cannot prevent `TopMost = true` from affecting the user's Z-order
  - They cannot prevent `this.Activate()` from attempting focus acquisition
  - `JOB_OBJECT_UILIMIT_HANDLES` prevents accessing handles OUTSIDE the job, but the form's own handles (and thus focus within its own window) are unrestricted
- **Job Objects are not UI isolation:** Microsoft's own documentation states that Job Objects "can't provide any form of resource isolation -- processes within a Job Object can view and interact with resources outside of it." True isolation requires silos (which are kernel-mode container primitives, not accessible from user-mode).

**Verdict:** Job Objects provide useful resource containment but do NOT solve the focus-stealing problem. A form inside a Job Object can still call `SetForegroundWindow`, `this.Activate()`, and set `TopMost = true`, all of which can disrupt the user. Job Objects could be used as a complementary measure (e.g., to limit resource usage of launched apps) but not as a focus isolation solution.

| Criterion | Assessment |
|---|---|
| Works on user desktop | Yes |
| Works in CI | Yes |
| Prevents focus stealing | **No** (cannot block SetForegroundWindow/Activate/TopMost) |
| Admin required | No |
| Complexity | Low-medium |
| Licensing | Win32 API, no concerns |

---

## 3. Focus Stealing Analysis (Critical for User Desktop)

This section analyzes the specific focus-stealing risks when the MCP launches a WinForms app on the user's active desktop.

### 3.1 The Problem

When a developer is typing in their IDE and the MCP launches a WinForms app for automation/screenshot, the launched app can steal focus through multiple mechanisms:

| Mechanism | Who Calls It | Risk Level |
|---|---|---|
| Process launch itself | OS / MCP server | High -- new process gets foreground right |
| `this.Activate()` in Form.Load | Form under test | High |
| `this.Focus()` in Form.Load | Form under test | Medium |
| `textBox1.Focus()` / `textBox1.Select()` | Form under test | Medium (triggers parent activation) |
| `TopMost = true` in designer | Form under test | High (forces Z-order above all windows) |
| `SetForegroundWindow` via P/Invoke | Form under test | High (if within timeout window) |
| `Form.Show()` / `Form.ShowDialog()` for child forms | Form under test | Medium |
| `MessageBox.Show()` | Form under test | High (modal, steals focus) |

### 3.2 Windows Foreground Lock Rules

Windows restricts which processes can call `SetForegroundWindow` successfully. A process can set the foreground window only if:
- The process is the current foreground process
- The process was started by the current foreground process
- The process received the last input event
- There is no current foreground window
- The foreground lock timeout has expired (default: 200ms after last user input)
- The process is being debugged

**Critical insight for off-screen positioning (Approach 7):** When the MCP server (foreground process) launches the WinForms app, the CHILD process inherits foreground rights from the parent. This means the launched app CAN successfully call `SetForegroundWindow`, `this.Activate()`, etc. during its startup window. Off-screen positioning via `SetWindowPos` happens AFTER launch, so there is a race condition:

1. MCP calls `Process.Start()` -- child gets foreground rights
2. Child's `Form.Load` fires, calls `this.Activate()` -- **focus stolen**
3. MCP calls `SetWindowPos` to move window off-screen -- too late

### 3.3 `LockSetForegroundWindow` API

```csharp
[DllImport("user32.dll")]
static extern bool LockSetForegroundWindow(uint uLockCode);
// LSFW_LOCK = 1, LSFW_UNLOCK = 2
```

The foreground process can call `LockSetForegroundWindow(LSFW_LOCK)` to disable ALL calls to `SetForegroundWindow` system-wide. The lock is automatically released when:
- The user presses ALT
- The user clicks a window
- The system itself changes the foreground window

**This helps but is not bulletproof:**
- It prevents `SetForegroundWindow` but NOT `TopMost = true` (which affects Z-order, not foreground status)
- The lock is global and temporary -- it could interfere with other apps the user is interacting with
- `this.Activate()` in WinForms calls `SetForegroundWindow` under the hood, so it IS blocked
- `textBox1.Focus()` within a non-foreground window does NOT call `SetForegroundWindow` -- it sets focus within the window's own context, which is harmless if the window is not foreground

### 3.4 Does Off-Screen Positioning Prevent Focus Stealing?

**Partially, but with gaps:**

| Code in Form Under Test | Off-Screen Alone | Off-Screen + LockSetForegroundWindow |
|---|---|---|
| `this.Focus()` | Safe -- focus is within the form | Safe |
| `textBox1.Focus()` | Safe -- internal focus change | Safe |
| `textBox1.Select()` | Safe -- internal focus change | Safe |
| `this.Activate()` | **STEALS FOCUS** (calls SetForegroundWindow) | **Blocked** (lock prevents it) |
| `TopMost = true` | **Window appears on top** at (-32000,-32000) -- usually invisible but the form is now above all windows in Z-order | **Still takes Z-order** (lock does not affect Z-order) |
| `SetForegroundWindow` P/Invoke | **STEALS FOCUS** | **Blocked** |
| `MessageBox.Show()` | **STEALS FOCUS** (modal dialog activates parent) | **Blocked** (modal still created but not foreground) |

### 3.5 The `TopMost = true` Problem

`TopMost = true` is particularly insidious:
- It uses `SetWindowPos(hWnd, HWND_TOPMOST, ...)` which affects Z-order, not foreground status
- `LockSetForegroundWindow` does NOT prevent this
- An off-screen window at (-32000, -32000) with `TopMost = true` is technically above all other windows, but since it is off-screen, it is not visible. **However**, if the window is not far enough off-screen, or if multi-monitor setups extend the desktop bounds, it could become visible.
- **Mitigation:** After moving the window off-screen, periodically check and re-apply the position, or use `SetWindowPos` to force `HWND_BOTTOM` z-order.

### 3.6 Summary: Off-Screen Positioning is Good but Not Perfect

For the **common case** (forms that do not aggressively steal focus), off-screen positioning + `SW_SHOWNOACTIVATE` + `SWP_NOACTIVATE` works well:
- Internal focus calls (`textBox1.Focus()`, `textBox1.Select()`) are harmless
- The window is invisible to the user
- `PrintWindow` captures the content correctly

For **adversarial forms** (forms that call `this.Activate()`, set `TopMost = true`, or show `MessageBox`):
- `LockSetForegroundWindow` blocks most activation attempts
- `TopMost` remains a gap that requires periodic Z-order correction
- The `CreateDesktop` approach (Approach 9) is the only complete solution

---

## 4. Recommended Approach

### Primary Recommendation: Tiered Strategy

The solution should be implemented in tiers, with each tier adding more isolation. The first tier covers the common case and is sufficient for most forms. Later tiers address edge cases.

### Tier 1: Off-Screen Window + PrintWindow + Focus Lock (Covers ~95% of Forms)

**Target: User desktop and CI environments.**

Change the headless flag behavior from "hide the window" to "show the window off-screen with focus protection":

1. **Before launch:** Call `LockSetForegroundWindow(LSFW_LOCK)` to prevent the child process from stealing focus during startup.
2. **Launch:** Remove `CreateNoWindow = true` and `WindowStyle.Hidden`. Launch the app normally so it gets a real HWND.
3. **Immediately after HWND is available:** Call `SetWindowPos(hWnd, HWND_BOTTOM, -32000, -32000, 0, 0, SWP_NOACTIVATE | SWP_NOSIZE | SWP_SHOWWINDOW)` to move it off-screen and to the bottom of the Z-order.
4. **After positioning:** Call `LockSetForegroundWindow(LSFW_UNLOCK)` to restore normal foreground behavior for the user.
5. **For screenshots:** Use `PrintWindow` instead of FlaUI's `Capture()` / `CopyFromScreen()`.
6. **Periodic guard (optional):** On a timer or before each automation action, re-apply `SetWindowPos(HWND_BOTTOM, -32000, -32000, ...)` to counteract any `TopMost = true` or position changes the form may have applied.

**Why this is sufficient for most forms:**
- Forms that call `textBox1.Focus()` or `this.Focus()` in their Load event only change focus WITHIN the form -- no effect on the user.
- `LockSetForegroundWindow` blocks `this.Activate()` and `SetForegroundWindow` during the critical startup window.
- Off-screen at (-32000, -32000) is well outside any reasonable monitor configuration.
- `PrintWindow` captures content regardless of screen position.

### Tier 2: `CreateDesktop` Isolation (Complete Focus Isolation)

**Target: User desktop, for forms known to be aggressive with focus/TopMost.**

If Tier 1 proves insufficient (e.g., a form sets `TopMost = true` AND repositions itself), escalate to `CreateDesktop`:

1. Create a hidden desktop: `CreateDesktop("McpAutomation", ...)`.
2. Launch the app on the hidden desktop via `CreateProcess` with `STARTUPINFO.lpDesktop = "WinSta0\\McpAutomation"`.
3. Use `PrintWindow` (with `EnumDesktopWindows` to find the window) for screenshots.
4. Use FlaUI with `SetThreadDesktop` for element discovery (needs verification).
5. Clean up: `CloseDesktop` when done.

**This provides absolute isolation:** The form under test can call `this.Activate()`, set `TopMost = true`, show `MessageBox`, call `textBox1.Focus()` -- none of it affects the user's desktop because the form exists on a completely separate desktop surface.

**Trade-offs:**
- Cannot use `System.Diagnostics.Process.Start` -- must P/Invoke `CreateProcess` to set `lpDesktop`
- FlaUI cross-desktop compatibility needs testing
- More P/Invoke surface area to maintain and test

### Why NOT Docker or Job Objects

- **Docker** (Approach 8): Windows containers have no display session. `PrintWindow` returns black because the container runs in a non-interactive window station. Startup overhead (5-30s) far exceeds the 450ms target. Requires Docker Desktop as a dependency.
- **Job Objects** (Approach 11): Cannot prevent `SetForegroundWindow`, `this.Activate()`, or `TopMost = true`. They restrict resource usage and handle access, not UI focus. Useful as a complementary measure but not a solution to the core problem.
- **CreateWindowStation** (Approach 10): Goes too far -- `PrintWindow` only works in `WinSta0` (the interactive window station). A separate window station makes screenshot capture impossible.

---

## 5. Implementation Plan

### Phase 1: Tier 1 Implementation

#### File: `src/Rhombus.WinFormsMcp.Server/Automation/NativeMethods.cs` (NEW)

Create a new helper class for Win32 P/Invoke declarations and native window operations:

```csharp
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Rhombus.WinFormsMcp.Server.Automation;

/// <summary>
/// Native Win32 methods for window management and capture.
/// Provides focus-safe window positioning and PrintWindow-based screenshot capture.
/// </summary>
internal static class NativeMethods
{
    // --- P/Invoke declarations ---

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool LockSetForegroundWindow(uint uLockCode);

    // --- Constants ---

    private const uint PW_RENDERFULLCONTENT = 0x00000002;
    private static readonly IntPtr HWND_BOTTOM = new(1);
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint LSFW_LOCK = 1;
    private const uint LSFW_UNLOCK = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    // --- Public API ---

    /// <summary>
    /// Lock foreground window changes to prevent launched processes from stealing focus.
    /// Call before Process.Start(), unlock after positioning the window.
    /// </summary>
    public static void LockForeground() => LockSetForegroundWindow(LSFW_LOCK);

    /// <summary>
    /// Unlock foreground window changes. Call after the launched window is positioned.
    /// </summary>
    public static void UnlockForeground() => LockSetForegroundWindow(LSFW_UNLOCK);

    /// <summary>
    /// Move a window off-screen without activating it, at the bottom of Z-order.
    /// </summary>
    public static void MoveOffScreen(IntPtr hWnd)
    {
        SetWindowPos(hWnd, HWND_BOTTOM, -32000, -32000, 0, 0,
            SWP_NOACTIVATE | SWP_NOSIZE | SWP_SHOWWINDOW);
    }

    /// <summary>
    /// Capture a window to a Bitmap using PrintWindow.
    /// Works for off-screen, occluded, and background windows.
    /// </summary>
    public static Bitmap? CaptureWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return null;

        if (!GetWindowRect(hWnd, out var rect)) return null;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0) return null;

        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var gfx = Graphics.FromImage(bmp);
        var hdc = gfx.GetHdc();
        try
        {
            // Try PW_RENDERFULLCONTENT first (Win 8.1+), fall back to default
            if (!PrintWindow(hWnd, hdc, PW_RENDERFULLCONTENT))
                PrintWindow(hWnd, hdc, 0);
        }
        finally
        {
            gfx.ReleaseHdc(hdc);
        }

        return bmp;
    }
}
```

#### File: `src/Rhombus.WinFormsMcp.Server/Automation/AutomationHelper.cs` (MODIFY)

##### Change 1: Fix LaunchApp headless behavior with focus protection

```csharp
// BEFORE:
var psi = new ProcessStartInfo
{
    FileName = path,
    Arguments = arguments ?? string.Empty,
    WorkingDirectory = workingDirectory ?? string.Empty,
    UseShellExecute = false,
    CreateNoWindow = Headless,
    WindowStyle = Headless ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal
};

// AFTER:
var psi = new ProcessStartInfo
{
    FileName = path,
    Arguments = arguments ?? string.Empty,
    WorkingDirectory = workingDirectory ?? string.Empty,
    UseShellExecute = false,
    CreateNoWindow = false,  // Always create window for UIA access
    WindowStyle = ProcessWindowStyle.Normal
};

// Lock foreground to prevent child process from stealing focus during startup
if (Headless)
    NativeMethods.LockForeground();

try
{
    var process = Process.Start(psi)
        ?? throw new InvalidOperationException($"Failed to launch {path}");
    process.WaitForInputIdle(5000);

    // In headless mode, move window off-screen after it's created
    if (Headless && process.MainWindowHandle != IntPtr.Zero)
    {
        NativeMethods.MoveOffScreen(process.MainWindowHandle);
    }
}
finally
{
    if (Headless)
        NativeMethods.UnlockForeground();
}
```

##### Change 2: Add PrintWindow-based screenshot fallback in TakeScreenshot

```csharp
public void TakeScreenshot(string outputPath, AutomationElement? element = null)
{
    try
    {
        Bitmap? bitmap = null;

        if (element != null)
        {
            // In headless mode, prefer PrintWindow (works off-screen)
            if (Headless)
            {
                var hwnd = element.Properties.NativeWindowHandle.ValueOrDefault;
                if (hwnd != IntPtr.Zero)
                    bitmap = NativeMethods.CaptureWindow(hwnd);
            }

            // Non-headless or PrintWindow fallback: use FlaUI capture
            if (bitmap == null)
            {
                try { bitmap = element.Capture(); }
                catch { /* fall through */ }
            }

            // Last resort: PrintWindow (even in non-headless mode)
            if (bitmap == null)
            {
                var hwnd = element.Properties.NativeWindowHandle.ValueOrDefault;
                if (hwnd != IntPtr.Zero)
                    bitmap = NativeMethods.CaptureWindow(hwnd);
            }
        }
        else if (_automation != null)
        {
            // Desktop capture -- FlaUI only (PrintWindow requires a specific HWND)
            var desktop = _automation.GetDesktop();
            bitmap = desktop.Capture();
        }

        if (bitmap != null)
        {
            bitmap.Save(outputPath, ImageFormat.Png);
            bitmap.Dispose();
        }
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException($"Failed to take screenshot: {ex.Message}", ex);
    }
}
```

#### File: `src/Rhombus.WinFormsMcp.Server/Automation/IAutomationHelper.cs` (NO CHANGE)

The interface signature for `TakeScreenshot` does not change.

### Phase 2: Tier 2 Implementation (CreateDesktop -- future, if needed)

#### File: `src/Rhombus.WinFormsMcp.Server/Automation/HiddenDesktop.cs` (NEW, future)

Encapsulate hidden desktop lifecycle:

```csharp
/// <summary>
/// Manages a hidden Win32 desktop for complete UI isolation.
/// Windows created on this desktop cannot steal focus from the user's desktop.
/// </summary>
internal sealed class HiddenDesktop : IDisposable
{
    private IntPtr _hDesktop;
    private readonly string _desktopName;

    public string FullName => $"WinSta0\\{_desktopName}";

    public HiddenDesktop(string name = "McpAutomation")
    {
        _desktopName = name;
        _hDesktop = CreateDesktop(name, IntPtr.Zero, IntPtr.Zero, 0,
            GENERIC_ALL, IntPtr.Zero);
        if (_hDesktop == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    /// <summary>
    /// Launch a process on this hidden desktop.
    /// Requires CreateProcess P/Invoke (Process.Start doesn't expose lpDesktop).
    /// </summary>
    public Process LaunchProcess(string path, string arguments) { /* ... */ }

    /// <summary>
    /// Capture a specific window on this desktop using PrintWindow.
    /// </summary>
    public Bitmap? CaptureWindow(IntPtr hWnd) => NativeMethods.CaptureWindow(hWnd);

    /// <summary>
    /// Enumerate all top-level windows on this desktop.
    /// </summary>
    public IReadOnlyList<IntPtr> EnumerateWindows() { /* EnumDesktopWindows */ }

    public void Dispose()
    {
        if (_hDesktop != IntPtr.Zero)
        {
            CloseDesktop(_hDesktop);
            _hDesktop = IntPtr.Zero;
        }
    }
}
```

This would be activated by a configuration option (e.g., `--isolation=desktop`) for users who need maximum protection against focus stealing.

### Test Files

#### File: `tests/Rhombus.WinFormsMcp.Tests/NativeMethodsTests.cs` (NEW)

```csharp
[Test]
public void CaptureWindow_ReturnsNullForIntPtrZero()
{
    var result = NativeMethods.CaptureWindow(IntPtr.Zero);
    Assert.That(result, Is.Null);
}

[Test]
public void CaptureWindow_CapturesOffScreenWindow()
{
    using var form = new Form { Width = 200, Height = 100 };
    form.Show();
    NativeMethods.MoveOffScreen(form.Handle);
    using var bmp = NativeMethods.CaptureWindow(form.Handle);
    Assert.That(bmp, Is.Not.Null);
    Assert.That(bmp!.Width, Is.GreaterThan(0));
    form.Close();
}

[Test]
public void MoveOffScreen_PositionsWindowOffScreen()
{
    using var form = new Form { Width = 200, Height = 100 };
    form.Show();
    NativeMethods.MoveOffScreen(form.Handle);
    // Verify window is off-screen by checking its position
    // GetWindowRect should report negative coordinates
    form.Close();
}

[Test]
public void LockForeground_DoesNotThrow()
{
    // Verify the lock/unlock cycle completes without error
    NativeMethods.LockForeground();
    NativeMethods.UnlockForeground();
}
```

#### File: `tests/Rhombus.WinFormsMcp.Tests/AutomationHelperTests.Headless.cs` (MODIFY)

Update existing headless tests to verify the new behavior (off-screen positioning instead of hidden window).

---

## 6. Verification Steps

### Local Verification (User Desktop -- Primary)

1. **Build:** `dotnet build Rhombus.WinFormsMcp.sln`
2. **Unit tests:** `dotnet test Rhombus.WinFormsMcp.sln`
3. **Focus stealing test (manual):**
   ```
   1. Open Notepad and start typing
   2. In another terminal, launch MCP server in headless mode
   3. Send launch_app tool call to start TestApp
   4. Verify: Notepad NEVER loses focus, cursor stays in Notepad
   5. Verify: TestApp does NOT appear in taskbar or flash
   6. Verify: take_screenshot produces a valid PNG of the TestApp
   ```

4. **TopMost form test (manual):**
   ```
   1. Create a test form with TopMost = true and this.Activate() in Form_Load
   2. Repeat the focus stealing test above
   3. With Tier 1: verify focus is mostly protected (Activate blocked by lock,
      TopMost may briefly affect Z-order but window is off-screen)
   4. Document any gaps for Tier 2 justification
   ```

5. **PrintWindow capture test:**
   - Launch TestApp via MCP in headless mode
   - Call take_screenshot
   - Verify the PNG contains the actual form content (not black/blank)
   - Verify form controls (buttons, labels, textboxes) are visible in the screenshot

### CI Verification (Secondary)

6. **GitHub Actions:** Push to dev branch, verify:
   - All tests pass on `windows-latest`
   - Coverage remains at 100% (add COVERAGE_EXCEPTION for P/Invoke lines that can't be unit-tested on all platforms)
   - The form renderer tests still work (they don't use the new code path)

7. **Integration test on CI (future):**
   - Add a CI step that launches TestApp, takes a screenshot, and validates the PNG is non-zero bytes
   - This would catch regressions where the CI runner environment changes

### Edge Cases to Test

- Window that takes >5 seconds to appear (WaitForInputIdle timeout)
- Process that exits before screenshot is taken
- Form with zero width/height (should use fallback dimensions)
- Multiple monitors / high DPI (PrintWindow handles this automatically)
- Minimized window (PrintWindow returns blank -- document this limitation)
- Form with `TopMost = true` (verify Z-order correction works)
- Form that calls `this.Activate()` in Load event (verify LockSetForegroundWindow blocks it)
- Form that shows a MessageBox in Load event (verify it does not steal focus)
- Multiple concurrent MCP sessions launching apps simultaneously

---

## 7. Summary Table

| Approach | User Desktop | CI | FlaUI | Focus Safe | Admin | Recommended |
|---|---|---|---|---|---|---|
| 1. Virtual Display | No | No | Yes | N/A | Yes | No |
| 2. Session 0 | No | No | Partial | Yes | Yes | No |
| 3. Off-screen positioning | Yes | Yes | Discovery only | Partial | No | Yes (part of Tier 1) |
| 4. PrintWindow API | Yes | Yes | Complementary | Yes | No | Yes (primary capture) |
| 5. GitHub Actions desktop | N/A | Yes | Yes | N/A | No | Leverage it |
| 6. In-process rendering | Yes | Yes | N/A | Yes | No | Already done |
| 7. MoveWindow/ShowWindow | Yes | Yes | Yes | Partial | No | Yes (part of Tier 1) |
| 8. Docker containers | Yes | Difficult | No | Yes | No* | No |
| 9. CreateDesktop | Yes | Yes | Needs testing | **Yes** | No | Yes (Tier 2) |
| 10. CreateWindowStation | Yes | Yes | No | Yes | No | No (breaks PrintWindow) |
| 11. Job Objects | Yes | Yes | Yes | **No** | No | No (wrong tool) |

\* Docker Desktop is free for small business but has licensing costs for enterprise.

**Final recommendation:**

- **Tier 1 (implement now):** Off-screen positioning (3+7) + `LockSetForegroundWindow` + `PrintWindow` (4). This handles the common case where the form under test does not aggressively steal focus. Works on both user desktops and CI. Zero external dependencies, no admin required.

- **Tier 2 (implement if needed):** `CreateDesktop` (9) for complete UI isolation. This is the only approach that guarantees zero focus impact regardless of what the form under test does (`Activate()`, `TopMost`, `MessageBox`, etc.). Moderate implementation complexity due to P/Invoke requirements for `CreateProcess`.

- **Not recommended:** Docker (no display session in containers, huge overhead), Job Objects (cannot prevent focus stealing), CreateWindowStation (breaks PrintWindow).
