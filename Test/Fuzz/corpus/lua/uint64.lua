local a = uint64.parse("18446744073709551615")
local b = uint64.parse("0x10")
assert(uint64.tostring(a) == "18446744073709551615")
return uint64.compare(a, b), uint64.divide(a, b), uint64.remainder(a, b), pcall(uint64.divide, a, 0)
