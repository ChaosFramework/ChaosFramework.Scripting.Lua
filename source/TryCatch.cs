using NLua;

namespace ChaosFramework.Scripting.Lua
{
    class TryCatch
    {
        internal static object[] Try(LuaFunction action, LuaFunction onError)
        {
            object[] result;
            try
            {
                result = action.Call();
            }
            catch (System.Exception ex)
            {
                System.Text.StringBuilder exBldr = new();

                do
                    exBldr.AppendLine(ex.Message);
                while ((ex = ex.InnerException) != null);

                result = [exBldr.ToString()];
                onError.Call(result[0]);
            }
            return result;
        }
    }
}
