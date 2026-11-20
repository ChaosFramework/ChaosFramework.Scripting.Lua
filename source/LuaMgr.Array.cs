using System;
using System.Reflection;

namespace ChaosFramework.Scripting.Lua
{
    partial class LuaMgr
    {
        public static class Array
        {
            internal const string ARRAY_OBJECT_NAME = "Array";

            const BindingFlags FLAGS = BindingFlags.Public | BindingFlags.Static;

            internal static readonly MethodInfo alloc = typeof(Array).GetMethod(nameof(Alloc), FLAGS);
            internal static readonly MethodInfo fromArgs = typeof(Array).GetMethod(nameof(FromArgs), FLAGS);

            public static T[] Alloc<T>(int length) => new T[length];
            public static T[] FromArgs<T>(params T[] data) => data;

            internal static void AddArrayMethod(LuaMgr lua, string typeName, string funcName, MethodInfo method)
            {
                string backendFunc = lua.GetQualifiedName([]).backendFunctionName;
                lua.RegisterFunction(backendFunc, method);
                lua.lua.DoString($"{ARRAY_OBJECT_NAME}.{typeName}.{funcName} = function(...) return {backendFunc}(...); end");
            }
        }

        void InitArrays()
        {
            RegisterClass(LuaArrayTypeGenerator.luaArrayType, Array.ARRAY_OBJECT_NAME);
            MakeArrayType(typeof(byte), "byte");
            MakeArrayType(typeof(short), "short");
            MakeArrayType(typeof(ushort), "ushort");
            MakeArrayType(typeof(int), "int");
            MakeArrayType(typeof(uint), "uint");
            MakeArrayType(typeof(long), "long");
            MakeArrayType(typeof(ulong), "ulong");
            MakeArrayType(typeof(float), "float");
            MakeArrayType(typeof(double), "double");
            MakeArrayType(typeof(string), "string");
            MakeArrayType(typeof(object), "object");
        }

        void MakeArrayType(Type t, string alias)
        {
            string typeName = alias ?? t.Name;
            lua.DoString($"{Array.ARRAY_OBJECT_NAME}.{typeName} = {{}};");
            Array.AddArrayMethod(this, typeName, Array.alloc.Name, Array.alloc.MakeGenericMethod(t));
            Array.AddArrayMethod(this, typeName, Array.fromArgs.Name, Array.fromArgs.MakeGenericMethod(t));
        }
    }
}
