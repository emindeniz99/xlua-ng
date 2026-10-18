using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using XLua;

// A game assembly (asmdef SgGame, which references XLua), so the source
// generator runs in it as well as in Assembly-CSharp. prepare.py --source-gen
// copies it to Assets/SgGame.
namespace SgGame
{
    [LuaCallCSharp]
    public enum Tier { Low = 1, High = 2 }

    [LuaCallCSharp]
    public class Shop
    {
        public int Stock = 3;

        public Tier Level { get; set; }

        public Shop()
        {
            Level = Tier.Low;
        }

        public Shop(int stock)
        {
            Stock = stock;
            Level = Tier.Low;
        }

        public int Price(int count)
        {
            return count * (Level == Tier.High ? 20 : 10);
        }

        public int Price(int count, string coupon)
        {
            return Price(count) - (coupon == "half" ? Price(count) / 2 : 0);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string Caller()
        {
            return Calls.Of(typeof(Shop));
        }
    }

    // Who called into a fixture type: the first stack frame outside it. A
    // source-generated wrapper is XLua.Generated.<assembly>_<hash>.W_<type>_<hash>;
    // reflection and CodeEmit show xLua's own frames or a [dynamic] type.
    public static class Calls
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string Of(Type self)
        {
            var frames = new StackTrace(false).GetFrames();
            for (int i = 0; frames != null && i < frames.Length; i++)
            {
                var method = frames[i].GetMethod();
                var type = method == null ? null : method.DeclaringType;
                if (type == typeof(Calls) || type == self) continue;
                return type == null ? "(none)" : type.FullName + (type.Assembly.IsDynamic ? " [dynamic]" : "");
            }
            return "(none)";
        }
    }
}
