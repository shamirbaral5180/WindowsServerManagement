# Windows Server Management v1.0.2

Patch release fixing the IIS website list display and Create Website dialog.

## Fixed

- Loaded IIS websites now render in the table instead of being hidden by the empty-state overlay.
- The first loaded website is automatically selected, so its details and actions appear immediately.
- All website columns display: name, state, server, binding, and application pool.
- Create Website no longer crashes with:
  `A TwoWay or OneWayToSource binding cannot work on the read-only property 'IisRoot'`.
- Read-only values in the Create Website and Edit Bindings dialogs now use one-way bindings.

## Verification

- Connected to a real server profile and loaded 3 IIS websites successfully.
- Confirmed all 3 rows render with state and server details.
- Confirmed automatic selection populates the details/actions panel.
- Opened the Create Website dialog successfully without an error.
- Release build succeeds with zero warnings/errors.
- 33 automated tests pass.

## Install

Download and run the single self-contained Windows x64 executable. No installer or .NET runtime is required.
