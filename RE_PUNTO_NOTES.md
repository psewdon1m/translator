# Punto Switcher Reverse-Engineering Notes (Static)

Source inspected: `example/Punto Switcher` (Punto Switcher 4.4.2.334)

## What Was Confirmed

- `punto.exe` is `x86` GUI PE.
- `pshook.dll` (`x86`) and `pshook64.dll` (`x64`) are dedicated hook modules.
- Hook DLLs export a compact API (named exports), including:
  - `SetHook`
  - `SwitchLayout`
  - `ReloadLowLevelHooks`
  - `SetHookTimeout`
  - `EnableCapsLock`
  - `EnableShiftF12Support`
  - `EnableEscapeSupport`
  - `EnableDiaryTracking`
  - `GetCapsLockState`
  - `IsPasswordField`
  - `GetCaretRect`

This strongly supports a split architecture:
- main process (`punto.exe`) = UI/settings/logic
- hook DLL = low-level keyboard + layout interaction + caret/password detection

## Important Data Files (Reusable)

### `Data/default-conf.json`
Contains per-application strategy lists:

- `use_paste`
- `use_special_copy_paste` (+ optional delay)
- `use_hotkey_switching` (per Windows version)
- `unhook` (games / problem apps)
- `deactivate_menu`

This is likely a major reason Punto behaves more reliably than a generic one-path implementation.

### `Data/translit-en.dat` / `Data/translit-ru.dat`

- Plain text `key=value` dictionaries in `cp1251`
- Example:
  - `Abraham=Абрахам`
  - `Абрахам=Abraham`

These were integrated into our app via `Services/PuntoDataService.cs` and used by `TextTransformService`.

### `Data/triggers.dat`

- Text file in `cp1251`
- Contains many trigger patterns (garbled-looking in wrong encoding, readable in cp1251)
- Likely used to aggressively detect common mistyped keyboard-layout patterns

This was also integrated and used as an additional auto-convert signal.

### `Data/ps.dat`

- Not plain text
- Binary / encoded format
- Likely main heuristic dictionary / compressed tables
- Requires dedicated format analysis or dynamic tracing to decode safely

## Hook DLL Imports (What It Actually Uses)

From `pshook*.dll` imports (static parsing):

- `USER32`:
  - `SetWindowsHookExW`, `CallNextHookEx`, `UnhookWindowsHookEx`
  - `GetKeyboardState`, `ToAsciiEx`, `GetKeyboardLayout`
  - `GetGUIThreadInfo`, `AttachThreadInput`, `GetFocus`
  - `PostMessageW`, `PostThreadMessageW`
  - `GetForegroundWindow`, `GetClassNameW`
  - `GetKeyState`, `keybd_event`, `mouse_event`
- `KERNEL32`, `ADVAPI32`, `SHLWAPI` (supporting infra, IPC, sync, diagnostics)

Inference:
- Punto uses low-level hooks + thread/focus/caret inspection
- It likely chooses between direct switching, hotkey switching, and paste-like replacement paths per app

## Main EXE Imports (Relevant Parts)

`punto.exe` imports include:

- `RegisterHotKey`, `UnregisterHotKey`
- `SetWindowsHookExW`, `CallNextHookEx`
- `SendInput`
- clipboard APIs (`OpenClipboard`, `GetClipboardData`, `SetClipboardData`, etc.)
- layout APIs (`GetKeyboardLayout`, `ActivateKeyboardLayout`, `GetKeyboardLayoutList`)
- `PlaySoundW`

Inference:
- Punto mixes multiple mechanisms, not a single monolithic approach

## Practical Lessons For Our Implementation

1. Do not rely on one global strategy for all apps.
2. Keep hotkey detection and text replacement separate (our earlier coupling caused regressions).
3. Add per-app policy:
   - `clipboard replacement`
   - `special clipboard replacement + delay`
   - `direct input replacement`
   - `disable hook`
4. Keep translit dictionary and trigger patterns external and reloadable.

## Ghidra Deep Dive (`pshook64.dll`) - Export + Internal Flow

Headless decompilation (Ghidra) on `pshook64.dll` confirmed several implementation details:

### `SwitchLayout`

- `SwitchLayout(LPARAM hkl)` does not directly call `ActivateKeyboardLayout`.
- It resolves a target window in this order:
  1. `GetGUIThreadInfo(...).hwndCaret` (preferred)
  2. `GetGUIThreadInfo(...).hwndFocus`
  3. `AttachThreadInput + GetFocus()` fallback
  4. active/foreground fallback
- Then sends:
  - `PostMessageW(target, WM_INPUTLANGCHANGEREQUEST (0x50), 0, hkl)`

This is a strong hint that our app should prefer `WM_INPUTLANGCHANGEREQUEST` to the focused control instead of only switching thread/global layout.

### Hook reload / lifecycle

- `ReloadLowLevelHooks()` calls internal hook installer that:
  - serializes setup with a named mutex:
    - `GLOBAL_PSHook64.dll_Hook`
  - unhooks old hooks (tolerates `ERROR_INVALID_HOOK_HANDLE` = `0x57c`)
  - installs two global LL hooks:
    - `WH_KEYBOARD_LL (13)`
    - `WH_MOUSE_LL (14)`

This confirms Punto uses both keyboard and mouse low-level hooks and safely reloads them.

### Keyboard hook callback behavior (high level)

- Keyboard LL callback (`FUN_1800020d0`) first checks internal "busy/reentrancy" flag (`+0x40` in state).
- It gates processing by foreground-window policy (`FUN_180003420`).
- If allowed, it calls an internal event processor (`FUN_180002dd0`).
- If processor returns non-zero byte, callback returns `1` (consumes event); otherwise it calls `CallNextHookEx`.

This matches Punto's selective suppression model (consume only when it intentionally injected/converted something).

### Process / app filtering gate

- `FUN_180003420(HWND)` gets process ID of foreground window and opens process with query rights.
- Calls another helper on process handle (`FUN_1800032c0`) and returns inverted result.
- Likely purpose: skip problematic/excluded process types (config-driven `unhook` / special modes / security context).

### Event processor hints (`FUN_180002dd0`)

- Recognizes synthetic/injected events using sentinel `dwExtraInfo == 0x87654321`
  - returns sentinel-like value `0x87654300` for self-generated events
- Temporarily raises thread priority while processing keyboard event:
  - thread priority set to `15`
  - process priority calls observed too
- Delegates into deeper helpers (`FUN_180002ee0`, `FUN_1800028a0`)
  - likely fast-path filter + main key processing/state machine

The `0x87654321` sentinel is especially important: Punto tags its own synthetic key events and filters them in the hook to avoid recursion.

### Host <-> Hook message protocol (confirmed pattern)

Decompiling deeper helpers shows the hook DLL offloads a lot of logic to a host window (`DAT_18001dc40 + 0x10`) using custom registered messages and timeouts.

- `FUN_1800043a0(...)`:
  - wrapper over `SendMessageTimeoutW(hostWnd, customMsg, wParam, lParam, timeout)`
  - timeout is configurable via `SetHookTimeout` (`default 2000ms`)
  - returns host response (`LRESULT`) or `0`

- `FUN_1800026a0(...)`:
  - resolves focused target (`caret/focus/AttachThreadInput fallback`)
  - `PostMessageW(hostWnd, DAT_18001dbcc, targetHwnd, keyOrAction)`
  - appears to push key chars/actions to host-side buffer/logic

- `FUN_180002780(...)`:
  - resets password/field flag (`state +0x44 = 0`)
  - notifies host (`DAT_18001dbc8`)
  - posts a synthetic action via `FUN_1800026a0(..., 10)` (likely buffer reset/newline marker)

- `FUN_180002200(...)`:
  - `PostMessageW(hostWnd, DAT_18001dbc4, actionCode, 0)`
  - likely helper for special Win-key/system combo emulation notifications

This strongly suggests Punto keeps the hook DLL minimal and stateful, while complex text logic sits in the main process and communicates via custom window messages.

### Character extraction path (confirmed)

- `FUN_1800027c0(...)`:
  - `GetKeyboardState(...)`
  - asks host for target keyboard layout (`DAT_18001dbbc` via `SendMessageTimeoutW`)
  - runs `ToAsciiEx(vk, scan, keyState, ...)`
  - returns produced character or `0xFFFE` sentinel

So Punto does not rely only on current thread layout; it queries the host-side context and converts keys with `ToAsciiEx`.

### Main keyboard state machine (`FUN_1800028a0`) - what is clear

- Tracks:
  - `Shift` state
  - `CapsLock` cached state
  - key-up/key-down flags
  - per-key metadata packed into a `uVar13` bitfield
- Ignores/resets on many control/navigation keys:
  - `Esc`, `F1..F24`, navigation cluster, modifiers, Win keys, etc.
- Handles `Ctrl+V/C/X/Y/Z/A` specially (flush path) to avoid corrupting buffer tracking
- Uses focused-window resolver and host callbacks around `Enter`, `Backspace`, and normal chars
- Uses `GetKeyboardLayout(threadId)` and has locale-specific branches (several HKL language IDs hardcoded)
  - likely punctuation/case handling quirks for specific layouts

### Process gate (`FUN_1800032c0`) is architecture filtering

`FUN_1800032c0(processHandle)` is not app blacklisting itself.
It checks WOW64 / native system architecture and returns an architecture-compatibility result.

Observed behavior:
- uses `IsWow64Process` when available
- falls back to `GetNativeSystemInfo`
- result is used by `FUN_180003420(...)` gate (inverted)

Interpretation:
- hook processing is gated by cross-bitness compatibility / environment constraints
- app-specific policy likely lives elsewhere (config + host-side logic), not in this helper

### CapsLock handling

- `EnableCapsLock(false)` checks current CapsLock state and can synthesize a CapsLock press/release via `keybd_event`
- `GetCapsLockState()` and `EnableCapsLock()` operate on cached hook state too

This supports our plan to keep case-fix and caps behavior isolated in a stateful service.

### Mouse hook callback behavior (high level)

- Mouse LL callback posts a custom registered message to a target window stored in hook state
- Then forwards to `CallNextHookEx`

Likely used to refresh caret/focus/selection tracking and UI-side state via async message protocol.

## Legal / Licensing Note

Punto Switcher is proprietary software. Reusing ideas/behavior patterns is fine, but shipping proprietary data files
(`translit*.dat`, `triggers.dat`, `ps.dat`) in a public/open redistribution may have licensing/copyright implications.
For personal/local experimentation this is lower risk, but verify before distribution.

## Next RE Targets (Recommended)

1. Parse and apply `default-conf.json` app-strategy lists in our runtime.
2. Add per-app diagnostics to log which replacement strategy succeeds.
3. Investigate `ps.dat` format (entropy/header/signatures/possible compression).
4. Decompile deeper helpers (`FUN_180002ee0`, `FUN_1800028a0`, `FUN_1800032c0`) to recover:
   - event classification
   - app filtering logic
   - password/caret/menu detection
5. Compare `pshook.dll` (x86) vs `pshook64.dll` for simpler decompilation / symbol variance.
