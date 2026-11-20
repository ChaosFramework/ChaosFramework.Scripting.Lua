using System;
using ChaosFramework.Components;
using ChaosFramework.Core;
using NLua;
using Flags = System.Reflection.BindingFlags;
using SysCol = System.Collections.Generic;

namespace ChaosFramework.Scripting.Lua
{
    public abstract class DelayClass
    {
        class LuaDelay : Component
        {
            public DelayClass delayer;
            public bool catchException = false;
            float timer = float.NaN;
            float originalTimer;
            LuaFunction func;

            protected override void Create(CreateParameters cparams)
            {
                CParams<float, LuaFunction> args = CreateParameters.RequireAs<float, LuaFunction>(cparams);
                originalTimer = timer = args.v1;
                func = args.v2;
            }

            public override void SetUpdateCalls()
            {
                base.SetUpdateCalls();
                delayer.RegisterUpdate(Update);
            }

            void Update()
            {
                if (!delayer.lua.alive)
                {
                    Dispose();
                    return;
                }
                if ((timer -= ftime) < 0)
                {
                    LuaMgr.OverloadFunctionInfo x = delayer.lua.GetQualifiedName([]);
                    delayer.lua.SetObject(x.backendFunctionName, func);
                    delayer.InvokeCode(
                        catchException
                            ? $@"
                                    try(
                                        function() {x.backendFunctionName}(); end,
                                        function(e) print(e); end
                                    );"
                            : $"{x.backendFunctionName}();"
                        , originalTimer);
                    delayer.lua.DoString($"{x.backendFunctionName} = nil;");
                    Dispose();
                }
            }
        }

        const Flags DELAY_FUNCTION_BINDING = Flags.NonPublic | Flags.Static;
        static readonly SysCol.Dictionary<LuaMgr, DelayClass> instances = [];

        static void Delay(float time, LuaFunction func)
        {
            LuaMgr lua = AccessScope<LuaMgr>.current;
            DelayClass delayer = instances[lua];
            LuaDelay x = delayer.GetTargetScene().AddComponent<LuaDelay>(CreateParameters.Create(time, func));
            x.delayer = delayer;
        }

        static void TryDelay(float time, LuaFunction func)
        {
            LuaMgr lua = AccessScope<LuaMgr>.current;
            DelayClass delayer = instances[lua];
            LuaDelay x = delayer.GetTargetScene().AddComponent<LuaDelay>(CreateParameters.Create(time, func));
            x.delayer = delayer;
            x.catchException = true;
        }

        readonly LuaMgr lua;

        public DelayClass(LuaMgr lua)
        {
            if (instances.ContainsKey(lua))
                throw new InvalidOperationException("Multiple delay instances per lua context not allowed.");

            this.lua = lua;
            instances[lua] = this;

            void OnDispose()
            {
                instances.Remove(this.lua);
                this.lua.RemoveOnDispose(OnDispose);
            }
            this.lua.AddOnDispose(OnDispose);

            lua.RegisterFunction("delay", typeof(DelayClass).GetMethod(nameof(Delay), DELAY_FUNCTION_BINDING));
            lua.RegisterFunction("tryDelay", typeof(DelayClass).GetMethod(nameof(TryDelay), DELAY_FUNCTION_BINDING));
        }

        protected abstract Scene GetTargetScene();
        public abstract void RegisterUpdate(Action update);
        public abstract void InvokeCode(string code, float timer);
    }
}
