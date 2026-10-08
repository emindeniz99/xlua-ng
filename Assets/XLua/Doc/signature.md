# Source Code Signatures

This page translates the historical upstream xLua signature instructions.

## Purpose

Source code signatures can help prevent files from being tampered with by an attacker during transfer.

## Using xLua's signature feature

1. Use `Tools/KeyPairsGen.exe` to generate a public/private key pair. `key_ras` contains the private key and `key_ras.pub` contains the public key. Keep both files safe; the private key is security-sensitive and must be kept confidential.
2. Use `Tools/FilesSignature.exe` to sign the source files:
   - Place `key_ras` in the tool's working directory.
   - Pass the source directory and destination directory as arguments. The tool signs every file with the `.lua` extension in the source directory and its subdirectories, and writes the signed files under the same directory structure in the destination directory.
3. Wrap your existing `CustomLoader` with `SignatureLoader` and use the wrapper. `SignatureLoader` has a constructor that takes the public key (the contents of `key_ras.pub`) and the original loader.
