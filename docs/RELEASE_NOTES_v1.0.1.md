# Windows Server Management v1.0.1

Patch release fixing password entry in the server connection dialog.

## Fixed

- The password and private-key passphrase fields now update the view model immediately through direct `PasswordChanged` handlers.
- Test Connection no longer reports "A password is required" when a password has been entered.
- Save now persists the entered password correctly as a DPAPI-encrypted credential.

## Verification

- Test Connection with a typed password reaches the SSH connection stage and reports the real network error instead of password validation.
- Saving the same profile creates `secrets.dat`, and the plaintext password does not appear in the file.
- Release build succeeds with zero warnings/errors.
- 33 automated tests pass.

## Install

Download and run the single self-contained Windows x64 executable. No installer or .NET runtime is required.
