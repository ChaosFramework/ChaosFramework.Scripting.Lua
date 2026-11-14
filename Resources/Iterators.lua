function ipairs(t)
    local tp = getType(t)
    if tp.isArray then
        local function ipairs_it(t, i)
            if t.Length == i then
                return nil;
            else
                local v = t[i]
                i = i+1
                if v ~= nil then
                    return i,v
                else
                    return i,nil
                end
            end
        end
        return ipairs_it, t, 0
    else
        local enumerator = Collections.GetEnumerator(t);
        if enumerator ~= nil then
            local function ipairs_it(t, i)
                if enumerator:MoveNext() then
                    i = i+1
                    return i,enumerator.Current;
                else
                    return nil;
                end
            end
            return ipairs_it, t, 0
        else
            local function ipairs_it(t, i)
                i = i+1
                local v = t[i]
                if v ~= nil then
                    return i,v
                else
                    return nil
                end
            end
            return ipairs_it, t, 0
        end
    end
end
