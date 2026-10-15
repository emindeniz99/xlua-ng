local n = 0
while true do n = n + 1 if n > 100 then break end end
return pcall(function() while true do end end)
