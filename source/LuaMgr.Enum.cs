using System.Reflection;
using ChaosUtil.Reflection;

namespace ChaosFramework.Scripting.Lua
{
    partial class LuaMgr
    {
        public class EnumCapsule<T> where T : struct
        {
            // All these methods are registered for and called by lua

            public T[] GetValues() => Enum<T>.GetValues();
            public string[] GetNames() => Enum<T>.GetNames();

            public T Parse(T value)
                => value;
            public T? Parse(string value)
                => Enum<T>.TryParseFlags(value, out T result) ? result : null;
            public T? Parse(double value)
                => Cast.TryCast(typeof(T).GetEnumUnderlyingType(), value, out object parsed) ? (T)parsed : null;
        }

        internal static T ToUnderlying<T>(object value)
            where T : struct
            => (T)value;

        internal static double EnumToNumber(System.Type t, object value)
            => t.GetEnumUnderlyingType().Name switch
            {
                nameof(System.SByte) => ToUnderlying<sbyte>(value),
                nameof(System.Int16) => ToUnderlying<short>(value),
                nameof(System.Int32) => ToUnderlying<int>(value),
                nameof(System.Int64) => ToUnderlying<long>(value),
                nameof(System.Byte) => ToUnderlying<byte>(value),
                nameof(System.UInt16) => ToUnderlying<ushort>(value),
                nameof(System.UInt32) => ToUnderlying<uint>(value),
                nameof(System.UInt64) => ToUnderlying<ulong>(value),
                _ => throw new System.NotSupportedException($"Enums must not have the underlying type {t.Name}."),
            };

        public void RegisterEnum<T>(string alias = null)
            where T : struct
        {
            System.Type t = typeof(T);
            if (!t.IsEnum)
                throw new System.ArgumentException($"{t.FullName} is not an enum type", nameof(T));

            string enumName = alias ?? t.Name;
            DoString($"{enumName} = {{}};");
            foreach (T value in Enum<T>.GetValues())
                lua.SetObjectToPath($"{enumName}.{value}", value);

            EnumCapsule<T> enumObject = new();
            string enumObjectPath = GetQualifiedName([]).backendFunctionName;
            lua.SetObjectToPath(enumObjectPath, enumObject);

            foreach (MethodInfo info in typeof(EnumCapsule<T>).GetMethods())
                lua.DoString($@"
                    {enumName}.{info.Name} = function(...)
                        return {enumObjectPath}:{info.Name}(...);
                    end
                    ");

            MakeArrayType(t, enumName);
            RegisterClassName(t, enumName);
        }
    }
}
