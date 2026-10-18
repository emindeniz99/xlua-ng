local b = buffer.create(1 << 19)
for i = 1, 1e6 do pcall(buffer.fill, b, 0, i) pcall(buffer.copy, b, 1, b, 0) local s = buffer.tostring(b) end
