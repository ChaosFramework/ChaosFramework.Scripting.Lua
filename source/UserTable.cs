using SysCol = System.Collections.Generic;

namespace ChaosFramework.Scripting.Lua
{
    public class UserTable
    {
        readonly SysCol.Dictionary<object, object> data = [];

        public object Get(object key) => data.TryGetValue(key, out object value) ? value : null;
        public void Set(object key, object value) => data[key] = value;
    }
}
