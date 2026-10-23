# Windows Server Management v1.2.0

Usability and appearance update.

## Light mode

- Added a soft blue-tinted window gradient and blue-tinted sidebar.
- Dashboard summary cards now use light blue, green, teal, purple, and amber accents.
- Retained the IIS-style compact tables and Actions panes.

## Resizable layout

- The main Connections sidebar can be resized by dragging its divider.
- The IIS Websites and Servers Actions panes can also be resized.
- Minimum and maximum widths prevent unusable layouts.

## Full-screen mode

- Added a full-screen button in the title bar.
- Press **F11** to enter/exit full screen.
- Press **Esc** to leave full screen.
- Full screen uses the current monitor and covers the taskbar.
- Minimize/maximize controls are disabled while in full screen.

## Verification

- Verified the full-screen button expands the app to the full monitor size (`1360x768` in the test environment).
- Verified Esc restores the previous window state and size.
- Verified light-mode rendering with the new color accents.
- Release build succeeds with zero warnings/errors.
- 35 automated tests pass.

## Install

Download and run the single self-contained Windows x64 executable. No installer or .NET runtime is required.
