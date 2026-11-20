using System;
using System.Reflection;
using ChaosFramework.Collections.Immutable;
using ChaosUtil.Reflection;
using SysCol = System.Collections.Generic;

namespace ChaosFramework.Scripting.Lua
{
    partial class LuaMgr
    {
        class ToNumber
        {
            delegate double DoubleCaster(object o);
            delegate ReturnType GenericCaster<ReturnType, ObjType>(ObjType o);

            static readonly ImmutableArray<Type> RETURN_TYPE_PRECEDENCE = new[]
            {
                typeof(double),
                typeof(float),
                typeof(long),
                typeof(int),
                typeof(short),
                typeof(sbyte),
                typeof(ulong),
                typeof(uint),
                typeof(ushort),
                typeof(byte)
            };

            static readonly SysCol.Dictionary<Type, DoubleCaster> casterCache = [];

            internal static void Register(LuaMgr luaMgr)
            {
                string luaToStringFn = luaMgr.GetQualifiedName([]).backendFunctionName;
                string csToStringFn = luaMgr.GetQualifiedName([]).backendFunctionName;
                luaMgr.lua.RegisterFunction(csToStringFn, typeof(ToNumber).GetMethod(
                    nameof(ToNumberLua),
                    BindingFlags.NonPublic | BindingFlags.Static,
                    null,
                    [typeof(object)],
                    null
                    ));

                luaMgr.lua.DoString($@"
                    {luaToStringFn} = tonumber;
                    tonumber = function(obj)
                        local res = {csToStringFn}(obj);
                        if res then
                            return res;
                        else
                            return {luaToStringFn}(obj);
                        end
                    end"
                    );
            }

            static double? ToNumberLua(object obj)
            {
                Type objType = obj.GetType();

                if (objType.IsEnum)
                    return EnumToNumber(objType, obj);

                for (Type currentBaseType = objType; currentBaseType != null; currentBaseType = currentBaseType.BaseType)
                    if (!casterCache.TryGetValue(currentBaseType, out DoubleCaster caster))
                    {
                        const BindingFlags CAST_METHOD_BINDING = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;
                        foreach (MethodInfo castMethod in currentBaseType.GetMethods(CAST_METHOD_BINDING))
                            if (castMethod.Name == "op_Implicit")
                                foreach (Type returnType in RETURN_TYPE_PRECEDENCE)
                                    if (castMethod.ReturnType == returnType)
                                    {
                                        ParameterInfo[] castMethodArgs = castMethod.GetParameters();
                                        if (castMethodArgs.Length == 1 && castMethodArgs[0].ParameterType == objType)
                                        {
                                            Type genericCasterType = typeof(GenericCaster<,>).MakeGenericType(returnType, currentBaseType);
                                            Delegate genericCaster = Delegate.CreateDelegate(genericCasterType, castMethod);
                                            double DoubleCaster(object x) => Cast.ForceCast<double>(genericCaster.DynamicInvoke(x));
                                            for (Type cacheType = objType; cacheType != currentBaseType; objType = objType.BaseType)
                                                casterCache[cacheType] = DoubleCaster;
                                            casterCache[currentBaseType] = DoubleCaster;
                                            return DoubleCaster(obj);
                                        }
                                    }
                        casterCache[currentBaseType] = null;
                    }
                    else if (caster != null)
                        return caster(obj);

                return null;
            }
        }
    }
}
