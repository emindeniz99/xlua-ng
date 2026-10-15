# Source Code Signatures

This page translates the historical upstream xLua signature instructions.

## Purpose

Source code signatures can help prevent files from being tampered with by an attacker during transfer.

## Using xLua's signature feature

0. Build the tools from the repository root with `python3 Tools/build_tools.py`.
1. Use `Tools/KeyPairsGen.exe` to generate a public/private key pair. `key_ras` contains the private key and `key_ras.pub` contains the public key. Keep both files safe; the private key is security-sensitive and must be kept confidential.
2. Use `Tools/FilesSignature.exe` to sign the source files:
   - Place `key_ras` in the tool's working directory.
   - Pass the source directory and destination directory as arguments. The tool signs every file with the `.lua` extension in the source directory and its subdirectories, and writes the signed files under the same directory structure in the destination directory.
3. Wrap your existing `CustomLoader` with `SignatureLoader` and use the wrapper. `SignatureLoader` has a constructor that takes the public key (the contents of `key_ras.pub`) and the original loader.

## Signature format

`KeyPairsGen` creates a 3072-bit RSA key. `FilesSignature` writes each signed file as the 4-byte magic `XLS2`, an RSA PKCS#1 v1.5 SHA-256 signature (384 bytes for a 3072-bit key), and then the unchanged script. The signature covers the script bytes only, so it does not bind a file to its module name: a signed file can be served under another module name.

Files signed by the upstream tools (a 128-byte RSA-1024 SHA-1 signature followed by the script) are rejected by default. To keep loading them while you re-sign, pass `true` as the third constructor argument: `new SignatureLoader(publicKey, loader, true)`. Re-sign and drop that flag as soon as you can; RSA-1024 and SHA-1 are no longer considered secure.

The key pair in `Examples/10_SignatureLoader/otherfiles` is a published demo key. Never sign production scripts with it.

`python3 Test/Native/signature_check.py --output <empty dir>` checks key generation, signing and verification end to end.
