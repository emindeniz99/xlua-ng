-- Strict CS mode (luacats_strict): names that are not configured are
-- reported. Each line marked `-- expect: <code>` must get exactly that
-- lua-language-server diagnostic, and no other line any.

local console = CS.System.Console -- expect: undefined-field
local missing = CS.LuaCATSSample.Square -- expect: undefined-field
local typo = CS.LuaCATSSampel -- expect: undefined-field
local inner = CS.LuaCATSSample.Circle.Stile -- expect: undefined-field
