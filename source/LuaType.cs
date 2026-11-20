using System;

namespace ChaosFramework.Scripting.Lua
{
    public class LuaType
    {
        internal static LuaType GetType(object luaObj) => new(luaObj.GetType());

        readonly Type myType;

        // All these properties are registered for and called by lua
        public string name => myType.Name;
        public bool isArray => myType.IsArray;
        public bool hasEnumerator => myType.IsSubclassOf(typeof(System.Collections.IEnumerable));
        public LuaType elementType => isArray ? new LuaType(myType.GetElementType()) : null;

        internal LuaType(Type t) { myType = t; }
    }
}
