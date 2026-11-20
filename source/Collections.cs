using System.Reflection;
using SysCol = System.Collections;

namespace ChaosFramework.Scripting.Lua
{
    public static class CollectionUtil
    {
        internal static void Register(LuaMgr lua)
        {
            lua.DoString("Collections = {}");
            foreach (MethodInfo m in typeof(CollectionUtil).GetMethods(BindingFlags.Public | BindingFlags.Static))
                lua.RegisterFunction($"Collections.{m.Name}", m);
        }

        public static LuaEnumerator GetEnumerator(SysCol.IEnumerable t)
        {
            SysCol.IEnumerator enumerator = t.GetEnumerator();
            return enumerator == null ? null : new LuaEnumerator(enumerator);
        }
    }

    public class LuaEnumerator
    {
        internal readonly SysCol.IEnumerator e;

#pragma warning disable ChaosCC0102 // Bad Identifier Case - Name is consumed by lua
        public object Current => e.Current;
#pragma warning restore ChaosCC0102 // Bad Identifier Case

        internal LuaEnumerator(SysCol.IEnumerator e) { this.e = e; }

        public bool MoveNext() => e.MoveNext();
    }
}
