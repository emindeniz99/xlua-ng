using UnityEngine;
using System.Collections;
using XLua;

namespace XLuaTest
{

    [GCOptimize(OptimizeFlag.PackAsTable)]
    public struct PushAsTableStruct
    {
        public int x;
        public int y;
    }

    public class ReImplementInLua : MonoBehaviour
    {

        // Use this for initialization
        void Start()
        {
            LuaEnv luaenv = new LuaEnv();
            //Both examples require generated code to run.
            //Example 1: customize Vector3
            //Keep the Vector3-to-userdata mapping, but implement its methods in Lua. xlua.genaccessor accesses memory directly without going through C#.
            //Bypassing C# improves performance and lets you omit the corresponding generated code to reduce the executable text section.
            //Userdata uses less memory than a table, but field access is slightly slower. Alternatively, map Vector3 to a table as in example 2.
            luaenv.DoString(@"
            function test_vector3(title, v1, v2)
               print(title)
               v1.x = 100
               print(v1.x, v1.y, v1.z)
               print(v1, v2)
               print(v1 + v2)
               v1:Set(v1.x - v2.x, v1.y - v2.y, v1.z - v2.z)
               print(v1)
               print(CS.UnityEngine.Vector3.Normalize(v1))
            end
            test_vector3('----before change metatable----', CS.UnityEngine.Vector3(1, 2, 3), CS.UnityEngine.Vector3(7, 8, 9))

            local get_x, set_x = xlua.genaccessor(0, 8)
            local get_y, set_y = xlua.genaccessor(4, 8)
            local get_z, set_z = xlua.genaccessor(8, 8)
            
            local fields_getters = {
                x = get_x, y = get_y, z = get_z
            }
            local fields_setters = {
                x = set_x, y = set_y, z = set_z
            }

            local ins_methods = {
                Set = function(o, x, y, z)
                    set_x(o, x)
                    set_y(o, y)
                    set_z(o, z)
                end
            }

            local mt = {
                __index = function(o, k)
                    --print('__index', k)
                    if ins_methods[k] then return ins_methods[k] end
                    return fields_getters[k] and fields_getters[k](o)
                end,

                __newindex = function(o, k, v)
                    if fields_setters[k] then fields_setters[k](o, v) else error('no such field ' .. k) end
                end,

                __tostring = function(o)
                    return string.format('vector3 { %f, %f, %f}', o.x, o.y, o.z)
                end,

                __add = function(a, b)
                    return CS.UnityEngine.Vector3(a.x + b.x, a.y + b.y, a.z + b.z)
                end
            }

            xlua.setmetatable(CS.UnityEngine.Vector3, mt)
            test_vector3('----after change metatable----', CS.UnityEngine.Vector3(1, 2, 3), CS.UnityEngine.Vector3(7, 8, 9))
        ");

            Debug.Log("++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++");

            //Example 2: customize the struct-to-table mapping
            //PushAsTableStruct becomes a Lua table. Add a SwapXY instance method, a Print static method, string formatting, and a constructor.
            luaenv.DoString(@"
            local mt = {
                __index = {
                    SwapXY = function(o) --Instance method
                        o.x, o.y = o.y, o.x
                    end
                },

                __tostring = function(o) --String formatting
                    return string.format('struct { %d, %d}', o.x, o.y)
                end,
            }

            xlua.setmetatable(CS.XLuaTest.PushAsTableStruct, mt)
            
            local PushAsTableStruct = {
                Print = function(o) --Static method
                    print(o.x, o.y)
                end
            }

            setmetatable(PushAsTableStruct, {
                __call = function(_, x, y) --Constructor
                    return setmetatable({x = x, y = y}, mt)
                end
            })
            
            xlua.setclass(CS.XLuaTest, 'PushAsTableStruct', PushAsTableStruct)
        ");

            PushAsTableStruct test;
            test.x = 100;
            test.y = 200;
            luaenv.Global.Set("from_cs", test);

            luaenv.DoString(@"
            print('--------------from csharp---------------------')
            assert(type(from_cs) == 'table')
            print(from_cs)
            CS.XLuaTest.PushAsTableStruct.Print(from_cs)
            from_cs:SwapXY()
            print(from_cs)

            print('--------------from lua---------------------')
            local from_lua = CS.XLuaTest.PushAsTableStruct(4, 5)
            assert(type(from_lua) == 'table')
            print(from_lua)
            CS.XLuaTest.PushAsTableStruct.Print(from_lua)
            from_lua:SwapXY()
            print(from_lua)
        ");

            luaenv.Dispose();
        }

        // Update is called once per frame
        void Update()
        {

        }
    }
}
