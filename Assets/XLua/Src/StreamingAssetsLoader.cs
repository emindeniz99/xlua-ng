/*
 * Tencent is pleased to support the open source community by making xLua available.
 * Copyright (C) 2016 THL A29 Limited, a Tencent company. All rights reserved.
 * Licensed under the MIT License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at
 * http://opensource.org/licenses/MIT
 * Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing permissions and limitations under the License.
*/

// Reads files below Application.streamingAssetsPath synchronously, for the
// StreamingAssets searcher (StaticLuaCallbacks.LoadFromStreamingAssetsPath, in
// the legacy preset) and for projects that add the reader themselves:
//
//     env.AddLoader(XLua.StreamingAssetsLoader.Load);
//
// (owner decision Q83 of 2026-10-29, docs/design/presets.md, "The
// StreamingAssets searcher"):
//
// - Android: through Android's AssetManager over AndroidJNI, which every
//   Android player has. Unity puts StreamingAssets under assets/ in the APK, in
//   the base module of an AAB, or in an install-time asset pack, all of which
//   the AssetManager sees. Before, the searcher used UnityEngine.WWW, which kept
//   the UnityWebRequest and TLS engine modules in every legacy Android player
//   (573,164 bytes of APK), spun until the request finished and slept 50 ms per
//   file.
// - Android with "Split Application Binary" (an APK with an OBB file): the
//   StreamingAssets are in the OBB, which the AssetManager does not see, and
//   Application.dataPath names the OBB. Then the reader uses UnityEngine.WWW
//   as before, through reflection, so that no other player references it;
//   StreamingAssetsObbLinkXml (Editor) keeps WWW in such builds.
// - WebGL: StreamingAssets are URLs on the web server and a synchronous
//   require cannot wait for a download. The searcher says so in require's
//   error message, and Load and ReadAllBytes throw NotSupportedException.
// - Everywhere else (the Editor, iOS, desktop): File.ReadAllBytes.

#if !XLUA_GENERAL
using System;
using System.IO;
#if UNITY_ANDROID && !UNITY_EDITOR
using System.Reflection;
using UnityEngine;
#endif

namespace XLua
{
    public static class StreamingAssetsLoader
    {
        // A LuaEnv.CustomLoader: the module a.b.c is the file a/b/c.lua below
        // StreamingAssets, as for the StreamingAssets searcher. Returns null when
        // there is no such file; filepath becomes "a/b/c.lua", the chunk name
        // without '@' and require's second result.
        public static byte[] Load(ref string filepath)
        {
            string file = FileName(filepath);
            byte[] bytes = ReadAllBytes(file);
            if (bytes != null)
            {
                filepath = file;
            }
            return bytes;
        }

        // The bytes of the file at path below StreamingAssets ('/' between the
        // folders, no leading '/'), or null when there is no such file.
        public static byte[] ReadAllBytes(string path)
        {
            if (path == null)
            {
                throw new ArgumentNullException("path");
            }
#if UNITY_WEBGL && !UNITY_EDITOR
            throw new NotSupportedException(WebGLMessage(path));
#elif UNITY_ANDROID && !UNITY_EDITOR
            return AndroidAssets.Read(path);
#else
            string fullPath = UnityEngine.Application.streamingAssetsPath + "/" + path;
            return File.Exists(fullPath) ? File.ReadAllBytes(fullPath) : null;
#endif
        }

        internal static string FileName(string module)
        {
            return module.Replace('.', '/') + ".lua";
        }

        internal static string WebGLMessage(string path)
        {
            return "StreamingAssets cannot be read synchronously on WebGL, so require cannot load '" + path
                + "' from there: load Lua from Resources, or download the files first and pass them through LuaEnv.AddLoader"
                + " (xLua's FAQ: \"How do I load Lua on each platform?\")";
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        // The AssetManager of the application context and the Java methods the
        // reader calls, looked up once; global references and method IDs are valid
        // on every thread. require may run on any thread (THREAD_SAFE), so the
        // reader attaches the calling thread to the Java VM and frees its local
        // references in a local frame.
        static class AndroidAssets
        {
            const int ACCESS_BUFFER = 3; // AssetManager.ACCESS_BUFFER: read the whole asset at once

            static readonly object initLock = new object();
            static volatile bool ready;
            static bool inObb;
            static IntPtr assetManager;
            static IntPtr openMethod, availableMethod, readMethod, closeMethod;

            static void Init()
            {
                if (ready) return;
                lock (initLock)
                {
                    if (ready) return;
                    // Application.dataPath names the OBB in a split application binary
                    // (Unity's documentation of Application.dataPath).
                    inObb = Application.dataPath.EndsWith(".obb", StringComparison.OrdinalIgnoreCase);
                    if (!inObb)
                    {
                        using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                        using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                        using (var context = activity.Call<AndroidJavaObject>("getApplicationContext"))
                        using (var assets = context.Call<AndroidJavaObject>("getAssets"))
                        {
                            IntPtr assetsClass = AndroidJNI.GetObjectClass(assets.GetRawObject());
                            openMethod = AndroidJNI.GetMethodID(assetsClass, "open", "(Ljava/lang/String;I)Ljava/io/InputStream;");
                            AndroidJNI.DeleteLocalRef(assetsClass);
                            IntPtr streamClass = AndroidJNI.FindClass("java/io/InputStream");
                            availableMethod = AndroidJNI.GetMethodID(streamClass, "available", "()I");
                            readMethod = AndroidJNI.GetMethodID(streamClass, "read", "([BII)I");
                            closeMethod = AndroidJNI.GetMethodID(streamClass, "close", "()V");
                            AndroidJNI.DeleteLocalRef(streamClass);
                            assetManager = AndroidJNI.NewGlobalRef(assets.GetRawObject());
                        }
                    }
                    ready = true;
                }
            }

            internal static byte[] Read(string path)
            {
                if (AndroidJNI.AttachCurrentThread() < 0)
                {
                    throw new IOException("xLua: cannot attach this thread to the Java VM to read StreamingAssets/" + path);
                }
                Init();
                if (inObb)
                {
                    return ObbAssets.Read(path);
                }
                if (AndroidJNI.PushLocalFrame(8) < 0)
                {
                    ClearJavaException();
                    throw new IOException("xLua: out of JNI local references reading StreamingAssets/" + path);
                }
                IntPtr stream = IntPtr.Zero;
                try
                {
                    IntPtr jpath = AndroidJNI.NewString(path);
                    try
                    {
                        stream = AndroidJNI.CallObjectMethod(assetManager, openMethod,
                            new jvalue[] { new jvalue { l = jpath }, new jvalue { i = ACCESS_BUFFER } });
                    }
                    catch (AndroidJavaException)
                    {
                        stream = IntPtr.Zero;
                    }
                    // AssetManager.open throws FileNotFoundException for a missing file.
                    if (ClearJavaException() || stream == IntPtr.Zero)
                    {
                        stream = IntPtr.Zero;
                        return null;
                    }
                    // The bytes left in an asset stream: its whole length here.
                    int size = AndroidJNI.CallIntMethod(stream, availableMethod, new jvalue[0]);
                    if (ClearJavaException() || size < 0) size = 0;
                    int chunk = size > 0 ? size : 8192;
                    IntPtr buffer = AndroidJNI.NewSByteArray(chunk);
                    var args = new jvalue[] { new jvalue { l = buffer }, new jvalue { i = 0 }, new jvalue { i = chunk } };
                    byte[] data = new byte[chunk];
                    int total = 0;
                    while (true)
                    {
                        int n = AndroidJNI.CallIntMethod(stream, readMethod, args);
                        if (ClearJavaException())
                        {
                            throw new IOException("xLua: reading StreamingAssets/" + path + " through Android's AssetManager failed");
                        }
                        // -1 at the end; never 0 for a positive length (InputStream.read).
                        if (n <= 0) break;
                        sbyte[] read = AndroidJNI.FromSByteArray(buffer);
                        if (total + n > data.Length)
                        {
                            Array.Resize(ref data, Math.Max(data.Length * 2, total + n));
                        }
                        Buffer.BlockCopy(read, 0, data, total, n);
                        total += n;
                    }
                    if (total != data.Length)
                    {
                        Array.Resize(ref data, total);
                    }
                    return data;
                }
                finally
                {
                    if (stream != IntPtr.Zero)
                    {
                        AndroidJNI.CallVoidMethod(stream, closeMethod, new jvalue[0]);
                        ClearJavaException();
                    }
                    AndroidJNI.PopLocalFrame(IntPtr.Zero);
                }
            }

            static bool ClearJavaException()
            {
                IntPtr exception = AndroidJNI.ExceptionOccurred();
                if (exception == IntPtr.Zero) return false;
                AndroidJNI.ExceptionClear();
                AndroidJNI.DeleteLocalRef(exception);
                return true;
            }
        }

        // StreamingAssets in the OBB of a split application binary: read with
        // UnityEngine.WWW, as xLua did on Android before, through reflection, so
        // that only the builds that keep WWW (StreamingAssetsObbLinkXml) pay for
        // the UnityWebRequest and TLS engine modules.
        static class ObbAssets
        {
            internal static byte[] Read(string path)
            {
                Type type = Type.GetType("UnityEngine.WWW, UnityEngine.UnityWebRequestWWWModule");
                if (type == null)
                {
                    throw new InvalidOperationException("xLua: this player's StreamingAssets are in its OBB file (Split Application Binary),"
                        + " which only UnityEngine.WWW reads, and the player has no WWW. In a project with the StreamingAssets searcher"
                        + " xLua keeps it; otherwise add <assembly fullname=\"UnityEngine.UnityWebRequestWWWModule\"><type fullname=\"UnityEngine.WWW\""
                        + " preserve=\"all\"/></assembly> to a link.xml, and keep the Unity Web Request WWW built-in package");
                }
                string url = Application.streamingAssetsPath + "/" + path;
                var www = (IDisposable)Activator.CreateInstance(type, url);
                try
                {
                    PropertyInfo isDone = type.GetProperty("isDone");
                    PropertyInfo error = type.GetProperty("error");
                    while (true)
                    {
                        if ((bool)isDone.GetValue(www, null) || !string.IsNullOrEmpty((string)error.GetValue(www, null)))
                        {
                            // Upstream's wait, kept for this path only.
                            System.Threading.Thread.Sleep(50);
                            if (!string.IsNullOrEmpty((string)error.GetValue(www, null)))
                            {
                                return null;
                            }
                            return (byte[])type.GetProperty("bytes").GetValue(www, null);
                        }
                    }
                }
                finally
                {
                    www.Dispose();
                }
            }
        }
#endif
    }
}
#endif
