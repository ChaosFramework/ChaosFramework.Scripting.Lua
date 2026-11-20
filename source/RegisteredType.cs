using System;

namespace ChaosFramework.Scripting.Lua
{
    public sealed class RegisteredType(string name, Type type)
    {
        public readonly string name = name;
        public readonly Type type = type;
    }
}
