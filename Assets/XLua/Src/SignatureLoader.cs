#if !UNITY_WSA || UNITY_EDITOR
using System.Security.Cryptography;
#else
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
#endif
using System;

namespace XLua
{
    // Signed files written by Tools/FilesSignature.exe start with the 4-byte
    // magic "XLS2", followed by an RSA PKCS#1 v1.5 SHA-256 signature as long as
    // the key (384 bytes for the default 3072-bit key) and then the script.
    // The upstream format (a 128-byte RSA-1024 SHA-1 signature, then the script)
    // is accepted only when allowLegacySha1 is true.
    public class SignatureLoader
    {
        static readonly byte[] MAGIC = { (byte)'X', (byte)'L', (byte)'S', (byte)'2' };
        const int LEGACY_SIGNATURE_LENGTH = 128;

        private LuaEnv.CustomLoader userLoader;
        private bool allowLegacySha1;
        private int signatureLength;
#if !UNITY_WSA || UNITY_EDITOR
        RSACryptoServiceProvider rsa;
#else
        CryptographicKey key;
        CryptographicKey legacyKey;
#endif

        public SignatureLoader(string publicKey, LuaEnv.CustomLoader loader) : this(publicKey, loader, false)
        {
        }

        public SignatureLoader(string publicKey, LuaEnv.CustomLoader loader, bool allowLegacySha1)
        {
            byte[] keyBlob = Convert.FromBase64String(publicKey);
#if !UNITY_WSA || UNITY_EDITOR
            rsa = new RSACryptoServiceProvider();
            rsa.ImportCspBlob(keyBlob);
            signatureLength = rsa.KeySize / 8;
#else
            var blob = CryptographicBuffer.CreateFromByteArray(keyBlob);
            key = AsymmetricKeyAlgorithmProvider.OpenAlgorithm(AsymmetricAlgorithmNames.RsaSignPkcs1Sha256)
                .ImportPublicKey(blob, CryptographicPublicKeyBlobType.Capi1PublicKey);
            if (allowLegacySha1)
            {
                legacyKey = AsymmetricKeyAlgorithmProvider.OpenAlgorithm(AsymmetricAlgorithmNames.RsaSignPkcs1Sha1)
                    .ImportPublicKey(blob, CryptographicPublicKeyBlobType.Capi1PublicKey);
            }
            signatureLength = (int)key.KeySize / 8;
#endif
            userLoader = loader;
            this.allowLegacySha1 = allowLegacySha1;
        }

        static bool HasMagic(byte[] data)
        {
            if (data.Length < MAGIC.Length) return false;
            for (int i = 0; i < MAGIC.Length; i++)
            {
                if (data[i] != MAGIC[i]) return false;
            }
            return true;
        }

        bool Verify(byte[] content, byte[] sig, bool legacy)
        {
#if !UNITY_WSA || UNITY_EDITOR
            return rsa.VerifyData(content, legacy ? "SHA1" : "SHA256", sig);
#else
            return CryptographicEngine.VerifySignature(legacy ? legacyKey : key,
                CryptographicBuffer.CreateFromByteArray(content), CryptographicBuffer.CreateFromByteArray(sig));
#endif
        }

        byte[] load_and_verify(ref string filepath)
        {
            byte[] data = userLoader(ref filepath);
            if (data == null)
            {
                return null;
            }

            bool legacy = !HasMagic(data);
            if (legacy && !allowLegacySha1)
            {
                throw new InvalidProgramException(filepath + " is not signed in the XLS2 (SHA-256) format!");
            }
            int offset = legacy ? 0 : MAGIC.Length;
            int length = legacy ? LEGACY_SIGNATURE_LENGTH : signatureLength;
            if (data.Length < offset + length)
            {
                throw new InvalidProgramException(filepath + " is shorter than its signature!");
            }

            byte[] sig = new byte[length];
            byte[] filecontent = new byte[data.Length - offset - length];
            Array.Copy(data, offset, sig, 0, length);
            Array.Copy(data, offset + length, filecontent, 0, filecontent.Length);

            if (!Verify(filecontent, sig, legacy))
            {
                throw new InvalidProgramException(filepath + " has invalid signature!");
            }
            return filecontent;
        }


        public static implicit operator LuaEnv.CustomLoader(SignatureLoader signatureLoader)
        {
            return signatureLoader.load_and_verify;
        }
    }
}
