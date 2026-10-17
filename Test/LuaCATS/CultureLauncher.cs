// Runs a .NET program in this process under a given culture, for
// Test/Native/luacats_check.py:
//
//     mono CultureLauncher.exe tr-TR XLuaGenerate.exe --luacats ...
//
// Mono on macOS takes its culture from the system settings and ignores LANG
// and LC_ALL, so the environment cannot select tr-TR there. Setting the
// culture here works on every OS: the current thread and every thread the
// program starts use it, and the program's exit code (Environment.ExitCode or
// the int that Main returns) and unhandled exceptions behave as under
// `mono program.exe`.
using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;

static class CultureLauncher
{
    static void Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: CultureLauncher <culture> <program.exe> [arguments]");
            Environment.ExitCode = 2;
            return;
        }
        // false: the culture's own data, without user overrides from the system.
        var culture = new CultureInfo(args[0], false);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;

        // LoadFrom, so that the program's own references (XLua.Mini.dll) are
        // found next to it.
        MethodInfo main = Assembly.LoadFrom(args[1]).EntryPoint;
        if (main == null)
        {
            Console.Error.WriteLine(args[1] + " has no entry point");
            Environment.ExitCode = 2;
            return;
        }
        string[] rest = args.Skip(2).ToArray();
        object result;
        try
        {
            result = main.Invoke(null, main.GetParameters().Length == 0 ? null : new object[] { rest });
        }
        catch (TargetInvocationException e)
        {
            ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            throw;
        }
        if (result is int)
        {
            Environment.ExitCode = (int)result;
        }
    }
}
