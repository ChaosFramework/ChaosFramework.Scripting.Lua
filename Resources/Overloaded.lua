function overloaded(name)
    local fns = {}
    local mt = {}
    local myName = name or '<anonymousFunc>';

    local function getDotNetName(arg)
        local dotNetType = getmetatable(arg)['__name'];
        dotNetType = string.gmatch(dotNetType, "%.[^.,]+,")(1);
        dotNetType = dotNetType:sub(2, dotNetType:len()-1 );
        dotNetType = dotNetType:gsub("%[$]", "table");
        return dotNetType;
    end

    local function overloadError(...)
        local argTable = {...}
        local argNum = #argTable;
        local errorMsg = 'You called '..myName..'(';
        for i,arg in ipairs(argTable) do
            local argTypeName = type(arg)
            if type(arg) == "userdata" then
                argTypeName = getDotNetName(arg);
            end
            errorMsg = errorMsg..argTypeName;
            if i ~= argNum then
                errorMsg = errorMsg..', ';
            end
        end

        local hasOverloads = false;
        for i,v in pairs(fns) do
            hasOverloads = true;
            break;
        end

        if hasOverloads then
            errorMsg = errorMsg..')\nAvailable overloads are:';
            for i,arg in pairs(fns) do
                errorMsg = errorMsg..'\n- '..myName..'('..i..')';
            end
        else
            errorMsg = errorMsg..')\nThere are no overloads for '..myName..'.';
        end
        return error(errorMsg);
    end

    function mt:__call(...)
        local signature = { }
        for i,arg in ipairs {...} do
            local typeName = type(arg)
            if type(arg) == "userdata" then
                typeName = getDotNetName(arg);
            end
            signature[i] = typeName
        end

        signature = table.concat(signature, ',')

        return (fns[signature] or self.default)(...)
    end

    function mt:__index(key)
            local signature = { }
            local function __newindex(self, key, value)
            signature[#signature+1] = key
            fns[table.concat(signature, ',')] = value
        end
        local function __index(self, key)
            signature[#signature+1] = key
            return setmetatable({}, { __index = __index, __newindex = __newindex })
        end
        return __index(self, key)
    end

    function mt:__newindex(key, value)
        if key=='void' then
            fns[''] = value;
        else
            fns[key] = value
        end
    end

    return setmetatable({ default = overloadError }, mt)
end
