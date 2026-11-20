
using System;

namespace ChaosFramework.Scripting.Lua
{
    public sealed class LuaTimeoutException(string message = "Lua code timed out. Lua state may be corrupted.", Exception inner = null)
        : Exception(message, inner);
}
