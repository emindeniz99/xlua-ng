assert(io == nil and getmetatable('') == false and _G == _ENV)
string.format = nil
return ('%d'):format(1)
