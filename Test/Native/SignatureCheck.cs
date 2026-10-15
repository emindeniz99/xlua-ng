using System;
using System.IO;
using System.Security.Cryptography;
using XLua;

// Exercises SignatureLoader against files produced by KeyPairsGen and
// FilesSignature. Run by signature_check.py in a scratch directory.
public static class SignatureCheck
{
    static int failures = 0;

    static void Expect(bool condition, string what)
    {
        Console.WriteLine((condition ? "PASS " : "FAIL ") + what);
        if (!condition) failures++;
    }

    static bool Throws(LuaEnv.CustomLoader loader, string name)
    {
        try { loader(ref name); return false; }
        catch (InvalidProgramException) { return true; }
    }

    static LuaEnv.CustomLoader FileLoader(string root, Func<byte[], byte[]> transform)
    {
        return (ref string filepath) => transform(File.ReadAllBytes(Path.Combine(root, filepath + ".lua")));
    }

    public static int Main(string[] args)
    {
        string dir = args[0];
        string publicKey = File.ReadAllText(Path.Combine(dir, "key_ras.pub"));
        byte[] original = File.ReadAllBytes(Path.Combine(dir, "src/module.lua"));
        string signedRoot = Path.Combine(dir, "signed");

        var rsa = new RSACryptoServiceProvider();
        rsa.ImportCspBlob(Convert.FromBase64String(publicKey));
        Expect(rsa.KeySize == 3072, "KeyPairsGen creates a 3072-bit key");
        byte[] signed = File.ReadAllBytes(Path.Combine(signedRoot, "module.lua"));
        Expect(signed.Length == 4 + 384 + original.Length, "signed file is magic + 384-byte signature + script");

        LuaEnv.CustomLoader loader = new SignatureLoader(publicKey, FileLoader(signedRoot, d => d));
        string name = "module";
        byte[] loaded = loader(ref name);
        Expect(loaded != null && Convert.ToBase64String(loaded) == Convert.ToBase64String(original), "valid file loads its original script");

        LuaEnv.CustomLoader tampered = new SignatureLoader(publicKey, FileLoader(signedRoot, d => { d[d.Length - 1] ^= 1; return d; }));
        Expect(Throws(tampered, "module"), "tampered script is rejected");

        LuaEnv.CustomLoader truncated = new SignatureLoader(publicKey, FileLoader(signedRoot, d => { Array.Resize(ref d, 100); return d; }));
        Expect(Throws(truncated, "module"), "file shorter than its signature is rejected");

        // Upstream format: 128-byte RSA-1024 SHA-1 signature, then the script.
        var legacyRsa = new RSACryptoServiceProvider(1024);
        string legacyKey = Convert.ToBase64String(legacyRsa.ExportCspBlob(false));
        byte[] legacySig = legacyRsa.SignData(original, "SHA1");
        byte[] legacyFile = new byte[legacySig.Length + original.Length];
        legacySig.CopyTo(legacyFile, 0);
        original.CopyTo(legacyFile, legacySig.Length);
        LuaEnv.CustomLoader legacyLoader = (ref string p) => (byte[])legacyFile.Clone();

        Expect(Throws(new SignatureLoader(legacyKey, legacyLoader), "module"), "legacy SHA-1 file is rejected");

        Console.WriteLine(failures == 0 ? "XLUA_SIGNATURE_CHECK_PASS" : "XLUA_SIGNATURE_CHECK_FAIL");
        return failures == 0 ? 0 : 1;
    }
}
