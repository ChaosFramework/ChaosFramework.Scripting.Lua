using System;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using ChaosFramework.Core;
using NLua;
using SysCol = System.Collections.Generic;

namespace ChaosFramework.Scripting.Lua
{
    public partial class LuaMgr : Disposable
    {
        class MethodComparer : SysCol.IComparer<MethodInfo>
        {
            public static MethodComparer instance = new();

            public int Compare(MethodInfo a, MethodInfo b)
                => a.Name.CompareTo(b.Name);
        }

        public readonly struct OverloadFunctionInfo
        {
            public readonly string backendFunctionName, argNames, argTypes;

            internal OverloadFunctionInfo(string backendFunctionName, string argNames, string argTypes)
            {
                this.backendFunctionName = backendFunctionName;
                this.argNames = argNames;
                this.argTypes = argTypes;
            }
        }

        public const string TOP_SECRET = "TopSecretBackend";

        static readonly SysCol.Dictionary<Type, string> luaTypes = [];

        static LuaMgr()
        {
            luaTypes[typeof(int)] = "number";
            luaTypes[typeof(float)] = "number";
            luaTypes[typeof(string)] = "string";
            luaTypes[typeof(bool)] = "boolean";
        }

        static T CreateStruct<T>() where T : struct => default;

        readonly string secretTmpField;
        readonly SysCol.Dictionary<string, Type> registeredTypes = [];
        readonly SysCol.Dictionary<Type, string> registeredMethods = [];
        readonly NLua.Lua lua;

        ulong topSecretBackendFunctionIndex = 1;

        public LuaMgr()
        {
            // TODO: parse array types and maybe derived types...
            lua = new();
            lua.State.Encoding = Encoding.UTF8;
            lua.DoString($"{TOP_SECRET} = {{}}");
            lua.DoString(Properties.Resources.Overloaded);
            lua.DoString(Properties.Resources.Iterators);
            lua.DoString("new = {}");
            secretTmpField = GetQualifiedName([]).backendFunctionName;

            InitArrays();
            RegisterClass(typeof(LuaType));
            CollectionUtil.Register(this);
            lua.RegisterFunction("getType", typeof(LuaType).GetMethod(
                nameof(LuaType.GetType),
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                [typeof(object)],
                null
                ));
            lua.RegisterFunction("try", typeof(TryCatch).GetMethod(
                nameof(TryCatch.Try),
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                [typeof(LuaFunction),
                typeof(LuaFunction)],
                null
                ));
            ToNumber.Register(this);
        }

        public bool MemberValid(MemberInfo member) => MemberValidation.MemberValid(lua, member);
        public bool FieldValid(FieldInfo field) => MemberValidation.FieldValid(lua, field);
        public bool PropertyValid(PropertyInfo property) => MemberValidation.PropertyValid(lua, property);
        public bool MethodValid(MethodInfo method) => MemberValidation.MethodValid(lua, method);
        public bool ParameterValid(ParameterInfo parameter) => MemberValidation.ParameterValid(lua, parameter);
        public bool ConstructorValid(ConstructorInfo ctor) => MemberValidation.ConstructorValid(lua, ctor);

        public Func<MemberInfo, bool> validateMember { get => lua.validateMember; set => lua.validateMember = value; }
        public Func<Type, bool> validateReturnType { get => lua.validateReturnType; set => lua.validateReturnType = value; }

        public System.Collections.IDictionaryEnumerator GetAllGlobalObjs() => lua.GetTable("_G").GetEnumerator();

        public SysCol.IEnumerable<RegisteredType> EnumerateRegisteredTypes()
        {
            foreach (SysCol.KeyValuePair<string, Type> kv in registeredTypes)
                yield return new RegisteredType(kv.Key, kv.Value);
        }

        public void RegisterFunction(string name, MethodBase meth) => lua.RegisterFunction(name, meth);

        public object[] DoString(string code)
        {
            using (new AccessScope<LuaMgr>(this))
                return lua.DoString(code);
        }

        public object[] DoString(string code, int timeout, Action overhead = null)
        {
            using (new AccessScope<LuaMgr>(this))
            {
                object[] result = null;
                Exception exception = null;
                void Work()
                {
                    try { result = DoString(code); }
                    catch (Exception ex) { exception = ex; }
                }
                Task task = Task.Run(Work);
                if (overhead == null)
                {
                    if (!task.Wait(timeout))
                        throw new LuaTimeoutException();
                }
                else
                {
                    System.Diagnostics.Stopwatch tm = System.Diagnostics.Stopwatch.StartNew();
                    while (!task.IsCompleted)
                    {
                        overhead?.Invoke();
                        if (tm.ElapsedMilliseconds >= timeout)
                            throw new LuaTimeoutException();
                    }
                }

                if (exception != null)
                    throw exception;
                else
                    return result;
            }
        }

        public void SetObject(string name, object obj) => lua.SetObjectToPath(name, obj);

        public string GetErrorMessage(Exception ex)
            => ex.InnerException != null && ex is NLua.Exceptions.LuaScriptException ex2 ? ex2.InnerException.Message : ex.Message;

        public void RegisterClassName(Type t, string alias = null)
        {
            alias ??= t.Name;
            if (registeredTypes.ContainsKey(alias))
                throw new InvalidOperationException("Class names can only be registered once.");

            registeredTypes[alias] = t;
        }

        public void RegisterClass(Type t, string alias = null)
        {
            alias ??= t.Name;
            RegisterClassName(t, alias);
            lua.DoString($"{alias} = {{}};");

            MakeArrayType(t, alias);
            MakeConstructors(t, alias);
            MakeStaticMethods(t, alias);
        }

        public void RegisterAssembly(Assembly ass)
        {
            foreach (Type t in ass.GetTypes())
                if (t.IsPublic)
                    RegisterClass(t);
        }

        void MakeConstructors(Type t, string alias)
        {
            string constructorRegisterCode = $"new.{alias} = overloaded('new.{t.Name}')";
            lua.DoString(constructorRegisterCode);

            if (!t.IsClass)
            {
                MethodInfo meth = typeof(LuaMgr).GetMethod(
                    nameof(CreateStruct),
                    BindingFlags.NonPublic | BindingFlags.Static
                    ).MakeGenericMethod(t);
                string backendFuncName = $"TopSecretBackendFunction{topSecretBackendFunctionIndex++}";
                string registerCommand = $"new.{alias}.void = function () return {backendFuncName}() end";
                lua.RegisterFunction(backendFuncName, meth);
                lua.DoString(registerCommand);
            }

            ConstructorInfo[] constructors = t.GetConstructors();
            foreach (ConstructorInfo constructor in constructors)
            {
                OverloadFunctionInfo name = GetQualifiedName(constructor.GetParameters());
                string registerCommand = $@"
                    new.{alias}{name.argTypes} = function {name.argNames}
                        return {name.backendFunctionName}{name.argNames}
                    end";
                lua.RegisterFunction(name.backendFunctionName, constructor);
                lua.DoString(registerCommand);
            }
        }

        void MakeStaticMethods(Type t, string alias)
        {
            static bool IsOut(ParameterInfo param) => param.IsOut || param.ParameterType.IsByRef;

            string lastRegisteredMethod = null;
            MethodInfo[] allMethods = t.GetMethods(BindingFlags.Static | BindingFlags.Public);
            System.Array.Sort(allMethods, MethodComparer.instance);
            foreach (MethodInfo method in allMethods)
                if (!method.IsSpecialName)
                    if (!method.GetParameters().Any(IsOut))
                    {
                        string luaIdentifier = $"{alias}.{method.Name}";
                        if (lastRegisteredMethod != method.Name)
                        {
                            lua.DoString($"{luaIdentifier} = overloaded('{luaIdentifier}')");
                            lastRegisteredMethod = method.Name;
                        }
                        OverloadFunctionInfo name = GetQualifiedName(method.GetParameters());
                        lua.RegisterFunction(name.backendFunctionName, method);
                        string registerCommand = $@"
                            {luaIdentifier}{name.argTypes} = function {name.argNames}
                                return {name.backendFunctionName}{name.argNames}
                            end";
                        lua.DoString(registerCommand);
                    }
        }

        public OverloadFunctionInfo GetQualifiedName(ParameterInfo[] parameters)
        {
            StringBuilder argTypes = new();
            StringBuilder argNames = new();

            if (parameters.Length == 0)
                argTypes.Append(".void");
            else
                foreach (ParameterInfo param in parameters)
                {
                    if (luaTypes.TryGetValue(param.ParameterType, out string luaTypeName))
                        argTypes.Append($".{luaTypeName}");
                    else
                        argTypes.Append($".{param.ParameterType.Name.Replace("[]", "table")}");
                    argNames.Append($", {param.Name}");
                }

            return new OverloadFunctionInfo(
                $"{TOP_SECRET}.X{topSecretBackendFunctionIndex++}",
                argNames.Length == 0 ? "()" : $"({argNames.ToString()[2..]})",
                argTypes.ToString()
                );
        }

        protected override void DoDispose()
        {
            base.DoDispose();
            lua.Close();
            lua.Dispose();
        }
    }
}
