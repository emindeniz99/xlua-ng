# Features

## Overview

### Lua runtimes

- Lua 5.3.6, 5.4.9, and 5.5.1 are available; the default CMake build selects 5.5.1.
- LuaJIT 2.1 is also present as an optional legacy build based on the bundled upstream LuaJIT.

### Historical Unity and platform list

The original upstream feature list stated support for all Unity3D versions and listed Windows 32-bit and 64-bit, Android, iOS 32-bit/64-bit/bitcode, macOS, UWP, and WebGL. This is a historical upstream list, not a statement of currently verified support in this fork. See the repository [README](../../../README.md) and [native build notes](../../../build/README.md) for current runtime, platform, and validation status.

### Interoperability

- Generated adapters or reflection.

### Ease of use

- Copy the Unity assets and install a rebuilt native plugin matching the selected runtime and target platform.
- Code generation is not required during development.
- Switch between generated code and reflection without changing the way they are called.
- Simpler APIs that avoid garbage collection.
- Straightforward menus.
- Multiple configurations can be divided by module; configuration can also be applied with attributes on target types.
- Automatically generates `link.xml` to prevent code stripping.
- The plug-in components are built with CMake.
- Core code does not depend on generated code, so the generated-code directory can be deleted at any time.

### Performance

- Lazy loading avoids the cost of types that are not used.
- Mapping Lua functions to C# delegates and Lua tables to interfaces can avoid C# GC allocations at the interface boundary.
- Generated conversion paths can pass primitive value types, enums, and eligible structs without C# GC allocations. Structs require the field restrictions and `GCOptimize` configuration described in the [GC optimization guide](GC_Optimization.md).
- `LuaTable` and `LuaFunction` provide typed APIs that avoid C# GC allocations on supported paths; this does not apply to every API. For example, `LuaFunction.Call` returns an allocated object array.
- Static analysis during code generation produces optimized code.
- Supports passing pointers between C# and Lua.
- Automatically releases references to destroyed `UnityEngine.Object` instances.

### Extensibility

- Add third-party Lua extensions without modifying xLua's code.
- The code-generation engine provides an API for further development.

## C# members that can be patched

- Constructors and finalizers.
- Instance and static methods, including generic methods.
- Operator overloads.
- Instance and static properties.
- Events.

## Loading Lua code

- Load a string, optionally execute it immediately, or return a delegate or `LuaFunction` that accepts script arguments when called.
- Load files in Unity's `Resources` directory directly with `require`.
- Use a custom loader that is invoked by Lua's `require`, receives the `require` argument, reads the Lua code, and returns it.
- Retain Lua's existing loading mechanisms.

## Calling C# from Lua

- Create C# objects.
- Access C# static and instance properties and fields, and call static and instance methods.
- Use inheritance: a child object can call parent methods and access parent properties; a child module can call parent static methods and access parent static properties.
- Use extension methods like ordinary instance methods.
- Use `out` and `ref` parameters: `out` maps to a Lua return value; `ref` maps to a Lua argument and a Lua return value.
- Call overloaded functions. Because Lua has fewer data types than C#, some overloads cannot be distinguished; extension methods can be used to call those cases.
- Use the supported overloaded operators: `+`, `-`, `*`, `/`, `==`, unary `-`, `<`, `<=`, `%`, and `[]`. Other operators can be called through extension methods.
- Omit C# parameters that have default values.
- Pass variadic arguments individually; they do not need to be packed into an array.
- Call generic methods by wrapping static methods or using extension methods for instance methods.
- Convert numbers or strings to enum values.
- Call C# delegates, add or remove them with `+` and `-`, and pass Lua functions to C# as delegates.
- Add and remove event callbacks.
- Pass 64-bit integers without GC allocations or precision loss. Lua 5.3 uses native 64-bit support; unsigned 64-bit integers are supported in the Java-style manner. 64-bit integers can also be used in arithmetic with Lua numbers.
- Convert Lua tables automatically to complex C# types. For example, `obj.complexField = {a = 1, b = {c = 1}}` can populate a C# field whose type is a struct or class nested two levels deep.
- Use `typeof` to obtain a C# `Type` object.
- Clone directly from Lua.
- Pass `decimal` values without GC allocations or precision loss.

## Calling Lua from C#

- Call Lua functions through a delegate or `LuaFunction`.
- Access Lua tables through the generic `LuaTable` `Get`/`Set` APIs, specifying key and value types to avoid GC allocations; through an interface marked `CSharpCallLua`; or by copying values into a struct or class.

## Lua virtual machine

- Read and set Lua virtual machine GC parameters.

## Tooling

- The Lua Profiler can sort by total function duration, average duration per call, or call count.
- It displays Lua function names and their source file and line, and marks C# functions as such.
- Supports debugging on devices.
