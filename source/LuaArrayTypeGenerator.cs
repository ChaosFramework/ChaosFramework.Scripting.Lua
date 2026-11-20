using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using ChaosFramework.Collections;
using LuaArray = ChaosFramework.Scripting.Lua.LuaMgr.Array;

namespace ChaosFramework.Scripting.Lua
{
    public static class LuaArrayTypeGenerator
    {
        const MethodAttributes INSTANCE_METHOD_ATTRIBUTES = MethodAttributes.Abstract | MethodAttributes.Virtual;

        static Type _luaArrayType;
        public static Type luaArrayType => _luaArrayType ??= GenerateLuaArrayType();

        static string SelectTypeName(Type type) => type.Name;

        static MethodAttributes SpecialName(MethodInfo m)
            => m.IsSpecialName
               ? MethodAttributes.SpecialName
               : 0;

        static PropertyAttributes SpecialName(PropertyInfo p)
            => p.IsSpecialName
               ? PropertyAttributes.SpecialName
               : PropertyAttributes.None;

        static Type GenerateLuaArrayType()
        {
            AssemblyBuilder assemblyBuilder =
#if NETCOREAPP
                AssemblyBuilder.DefineDynamicAssembly(
#elif !NETSTANDARD && !WINDOWS_UWP
                Thread.GetDomain().DefineDynamicAssembly(
#else
                #error Can't build dynamic assembly for this platform target!
#endif
                new AssemblyName($"{typeof(LuaArray).Assembly.GetName().Name}.Generated"),
                AssemblyBuilderAccess.Run
                );

            ModuleBuilder moduleBuilder = assemblyBuilder.DefineDynamicModule(
                $"{typeof(LuaArray).Module.Name}.Generated"
                );

            TypeBuilder typeBuilder = moduleBuilder.DefineType(
                LuaArray.ARRAY_OBJECT_NAME,
                TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed
                );

            foreach (MethodInfo method in typeof(Array).GetMethods(BindingFlags.Public | BindingFlags.Instance))
                if (method.DeclaringType == typeof(Array))
                    if (!method.IsSpecialName)
                        AddMethod(typeBuilder, method, INSTANCE_METHOD_ATTRIBUTES);

            foreach (MethodInfo method in typeof(LuaArray).GetMethods(BindingFlags.Public | BindingFlags.Static))
                if (!method.IsSpecialName)
                    AddMethod(typeBuilder, method, MethodAttributes.Static)
                    .GetILGenerator()
                    .ThrowException(typeof(NotSupportedException));

            foreach (PropertyInfo property in typeof(Array).GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (property.DeclaringType == typeof(Array))
                    if (!property.IsSpecialName)
                        AddProperty(typeBuilder, property);

            return typeBuilder.CreateType();
        }

        static string[] GetTypeParameterNames(MethodInfo method)
            => Array.ConvertAll(
                method.GetGenericMethodDefinition().GetGenericArguments(),
                SelectTypeName
                );

        static Type GetGenericParameterOrActualType(
            Type[] genericParameters,
            GenericTypeParameterBuilder[] genericParameterBuilders,
            Type type
            )
        {
            if (type.IsArray)
                return GetGenericParameterOrActualType(
                    genericParameters,
                    genericParameterBuilders,
                    type.GetElementType()
                    ).MakeArrayType();
            else if (type.IsPointer)
                return GetGenericParameterOrActualType(
                    genericParameters,
                    genericParameterBuilders,
                    type.GetElementType()
                    ).MakePointerType();
            else if (type.IsByRef)
                return GetGenericParameterOrActualType(
                    genericParameters,
                    genericParameterBuilders,
                    type.GetElementType()
                    ).MakeByRefType();
            else
            {
                int genericParameterIndex = Array.IndexOf(genericParameters, type);
                if (genericParameterIndex < 0)
                    return type;
                return genericParameterBuilders[genericParameterIndex];
            }
        }

        static ParameterAttributes GetParamAttrs(ParameterInfo p)
        {
            ParameterAttributes attr = ParameterAttributes.None;
            if (p.IsOptional) attr |= ParameterAttributes.Optional;
            if (p.HasDefaultValue) attr |= ParameterAttributes.HasDefault;
            return attr;
        }

        static MethodBuilder AddMethod(TypeBuilder typeBuilder, MethodInfo method, MethodAttributes methodAttrs)
        {
            MethodBuilder methodBuilder = typeBuilder.DefineMethod(
                method.Name,
                MethodAttributes.Public | SpecialName(method) | methodAttrs
                );

            Type[] genericParams = method.GetGenericArguments();

            GenericTypeParameterBuilder[] genericParamBuilders =
                method.IsGenericMethod
                ? methodBuilder.DefineGenericParameters(GetTypeParameterNames(method))
                : [];

            methodBuilder.SetReturnType(GetGenericParameterOrActualType(genericParams, genericParamBuilders, method.ReturnType));
            Type SelectParameterType(ParameterInfo p)
                => GetGenericParameterOrActualType(genericParams, genericParamBuilders, p.ParameterType);
            methodBuilder.SetParameters(Array.ConvertAll(method.GetParameters(), SelectParameterType));

            int parameterIndex = 1; // 0 would be the return value
            foreach (ParameterInfo param in method.GetParameters())
            {
                ParameterBuilder paramBuilder = methodBuilder.DefineParameter(
                    parameterIndex++,
                    GetParamAttrs(param),
                    param.Name
                    );
                if (param.HasDefaultValue)
                    paramBuilder.SetConstant(param.DefaultValue);
                foreach (CustomAttributeData attr in param.CustomAttributes)
                    paramBuilder.SetCustomAttribute(CreateAttributeBuilder(attr));
            }

            foreach (CustomAttributeData attr in method.CustomAttributes)
                methodBuilder.SetCustomAttribute(CreateAttributeBuilder(attr));

            return methodBuilder;
        }

        static PropertyBuilder AddProperty(TypeBuilder typeBuilder, PropertyInfo property)
        {
            static Type SelectParameterType(ParameterInfo param) => param.ParameterType;

            PropertyBuilder propertyBuilder = typeBuilder.DefineProperty(
                property.Name,
                SpecialName(property),
                property.PropertyType,
                Array.ConvertAll(property.GetIndexParameters(), SelectParameterType)
                );

            if (property.CanRead && property.GetMethod.IsPublic)
                propertyBuilder.SetGetMethod(
                    AddMethod(
                        typeBuilder,
                        property.GetMethod,
                        MethodAttributes.Public | INSTANCE_METHOD_ATTRIBUTES
                        ));

            if (property.CanWrite && property.SetMethod.IsPublic)
                propertyBuilder.SetSetMethod(
                    AddMethod(
                        typeBuilder,
                        property.SetMethod,
                        MethodAttributes.Public | INSTANCE_METHOD_ATTRIBUTES
                        ));

            foreach (CustomAttributeData attr in property.CustomAttributes)
                propertyBuilder.SetCustomAttribute(CreateAttributeBuilder(attr));

            return propertyBuilder;
        }

        static object SelectValue(CustomAttributeTypedArgument arg) => arg.Value;
        static T SelectMember<T>(Tuple<T, object> t) where T : MemberInfo => t.Item1;
        static object SelectValue<T>(Tuple<T, object> t) where T : MemberInfo => t.Item2;

        static CustomAttributeBuilder CreateAttributeBuilder(CustomAttributeData attr)
        {
            LinkedList<Tuple<FieldInfo, object>> fields = [];
            LinkedList<Tuple<PropertyInfo, object>> properties = [];

            foreach (CustomAttributeNamedArgument arg in attr.NamedArguments)
                if (!AddNamedArg(arg, fields))
                    if (!AddNamedArg(arg, properties))
                        throw new InvalidOperationException(
                            $"Unrecognized member type '{arg.MemberInfo.GetType().Name}' for member '{arg.MemberName}'."
                            );

            return new CustomAttributeBuilder(
                attr.Constructor,
                attr.ConstructorArguments.Select(SelectValue).ToArray(),
                properties.Select(SelectMember).ToArray(),
                properties.Select(SelectValue).ToArray(),
                fields.Select(SelectMember).ToArray(),
                fields.Select(SelectValue).ToArray()
                );
        }

        static bool AddNamedArg<Member>(
            CustomAttributeNamedArgument attrArg,
            LinkedList<Tuple<Member, object>> args
            ) where Member : MemberInfo
        {
            Member member = attrArg.MemberInfo as Member;
            if (member == null)
                return false;
            else
            {
                args.Add(new Tuple<Member, object>(member, attrArg.TypedValue.Value));
                return true;
            }
        }
    }
}
