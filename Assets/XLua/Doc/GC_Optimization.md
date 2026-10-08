# GC Optimization for Structs

## Garbage collection for complex value types

By default, xLua passes C# value types (structs) to Lua by reference. This requires boxing the value before passing it to Lua. Each boxing operation creates a new object; after Lua has finished using the value, that object can become eligible for collection and add to later GC work.

xLua provides a GC optimization for structs. With a small amount of configuration, structs that meet the requirements below can be passed to Lua without causing a C# GC allocation.

## Requirements

1. The struct may contain nested structs, but it and every nested struct may contain only these primitive types: `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float`, and `double`. Most Unity value types, including the `Vector` types, `Quaternion`, and `Color`, meet this requirement. User-defined structs can also qualify.
2. The struct must have the `GCOptimize` attribute configured. Several commonly used Unity structs, including the `Vector` types, `Quaternion`, and `Color`, already have this attribute. It can be configured through the configuration file or a C# attribute.
3. The struct must be included in the generated-code list wherever it is used.

## Configuration

See the **GCOptimize** section of [Configure](Configure_EN.md).
