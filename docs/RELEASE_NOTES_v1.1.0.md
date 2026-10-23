# Windows Server Management v1.1.0

Feature and UI release focused on an IIS-style management experience.

## UI

- Reworked the shell into an IIS-style management console:
  - Connections sidebar with Dashboard, Servers, IIS Websites, Activity Logs, and Settings.
  - Server tree in the sidebar with live connection indicators and target selection.
  - Compact command bars, flatter panels, square borders, IIS blue accents, and denser tables.
  - Servers page now uses a professional server table with an Actions pane.
  - IIS Websites page now uses a site table with a dedicated Actions pane.
- Added a prominent **Delete Site** button in the IIS toolbar and in the Actions pane.

## IIS website creation

- Physical path is now an editable field. It defaults to `<IIS root>\<site name>` and the folder is created on the selected remote server.
- Application pool options now match the IIS Add Application Pool dialog:
  - Application pool name
  - .NET CLR version: v4.0, v2.0, or No Managed Code
  - Managed pipeline mode: Integrated or Classic
  - Start application pool immediately
- Existing application pools are reused without changing their settings.
- Website creation rollback now covers folder and application-pool creation failures as well as site creation failures.

## Verification

- Loaded and displayed all 3 sites from the real WEBS01 connection.
- Verified site table columns including bindings, physical path, application pool, and server.
- Verified automatic selection and details/actions for the first site.
- Verified the new Create Website dialog opens without errors.
- Verified site name auto-populates the physical path (for example, `C:\inetpub\UiTestSite`) and application pool name.
- 35 automated tests pass; release build has zero warnings/errors.

## Install

Download and run the single self-contained Windows x64 executable. No installer or .NET runtime is required.
