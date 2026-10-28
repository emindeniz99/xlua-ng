using XLua;

// In Game.dll, outside xLua's assembly (asmdef and package layouts only; without
// asmdefs Game cannot reference xLua). Upstream's injector did nothing for these
// types outside the assembly that holds xLua, without a word: it compared [Hotfix]
// on a type with xLua's own attribute definition by reference, and it returned
// before injecting Inline types and generic methods. HotfixShapes.cs patches them.
namespace Game
{
    [Hotfix]
    public class TaggedShop
    {
        public int Price()
        {
            return 3;
        }
    }

    [Hotfix(HotfixFlag.Inline)]
    public class InlineShop
    {
        public int Price()
        {
            return 4;
        }
    }

    [Hotfix]
    public class GenericShop
    {
        public T Echo<T>(T value)
        {
            return value;
        }
    }
}
