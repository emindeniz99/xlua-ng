local fns = {uint64.parse, uint64.tostring, uint64.compare, uint64.divide, uint64.remainder,
  xlua.genaccessor, xlua.structclone, newstruct}
local values = {0, -1, 1.5, "7", "0x10", "", math.maxinteger, math.mininteger}
for _, f in ipairs(fns) do
  for _, a in ipairs(values) do
    for _, b in ipairs(values) do pcall(f, a, b) end
  end
end
