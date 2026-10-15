using System;
using System.IO;
using System.Security.Cryptography;

namespace XLua
{
    public class FilesSignature
    {
        // Must match SignatureLoader: magic, SHA-256 signature, then the script.
        static readonly byte[] MAGIC = { (byte)'X', (byte)'L', (byte)'S', (byte)'2' };

        static void usage()
        {
            Console.WriteLine("FilesSignature from_path to_path");
        }

        static void doSignature(string from, string to, RSACryptoServiceProvider rsa)
        {
            if (!Directory.Exists(to))
            {
                Directory.CreateDirectory(to);
            }
            foreach (var filename in Directory.GetFiles(from, "*.lua"))
            {
                byte[] filecontent = File.ReadAllBytes(filename);
                byte[] sig = rsa.SignData(filecontent, "SHA256");
                string sigFilePath = Path.Combine(to, Path.GetFileName(filename));
                using (FileStream fs = new FileStream(sigFilePath, FileMode.Create))
                {
                    fs.Write(MAGIC, 0, MAGIC.Length);
                    fs.Write(sig, 0, sig.Length);
                    fs.Write(filecontent, 0, filecontent.Length);
                    fs.Flush();
                }
            }
            foreach (var dir in Directory.GetDirectories(from))
            {
                string newDir = Path.Combine(to, new DirectoryInfo(dir).Name);
                doSignature(dir, newDir, rsa);
            }
        }

        public static void Main(string[] args)
        {
            // KeyPairsGen writes the private key to key_ras.
            if (!File.Exists("key_ras"))
            {
                Console.WriteLine("No key_ras file found!");
                return;
            }

            if (args.Length != 2)
            {
                usage();
                return;
            }

            RSACryptoServiceProvider rsa = new RSACryptoServiceProvider();
            rsa.FromXmlString(File.ReadAllText("key_ras"));
            doSignature(args[0], args[1], rsa);
        }
    }
}
