using XLua;
using System;
#if !XLUA_GENERAL
using UnityEngine;
#endif

[LuaCallCSharp]
public class LongStatic
{
    public static long LONG_MAX = System.Int64.MaxValue;
    public static ulong ULONG_MAX = System.UInt64.MaxValue;
}

[LuaCallCSharp]
public enum LuaTestType
{
	ABC = 0,
	DEF,
	GHI,
	JKL
};

[LuaCallCSharp]
public enum FirstPushEnum
{
	E1
};

[CSharpCallLua]
public delegate int TestDelegate(int x);
[CSharpCallLua]
public delegate int TestEvtHandler1(float y);
[CSharpCallLua]
public delegate int TestEvtHandler2(byte y, float z);

[LuaCallCSharp]
public class LuaTestObj
{
	public static LuaEnv luaEnv = LuaEnvSingletonForTest.Instance;
    public int testVar { get; set; }
    public int[] testArr = new int[3];
    public static LuaTestObj operator + (LuaTestObj a, LuaTestObj b)
    {
        LuaTestObj ret = new LuaTestObj();
        ret.testVar = a.testVar + b.testVar;
        return ret;
    }

    public static LuaTestObj operator +(LuaTestObj a, int b)
    {
        LuaTestObj ret = new LuaTestObj();
        ret.testVar = a.testVar + b;
        return ret;
    }

    public static LuaTestObj operator +(int a, LuaTestObj b)
    {
        LuaTestObj ret = new LuaTestObj();
        ret.testVar = a + b.testVar;
        return ret;
    }

    public static LuaTestObj operator - (LuaTestObj a, LuaTestObj b)
    {
        LuaTestObj ret = new LuaTestObj();
        ret.testVar = a.testVar - b.testVar;
        return ret;
    }

    public static LuaTestObj operator -(LuaTestObj a, int b)
    {
        LuaTestObj ret = new LuaTestObj();
        ret.testVar = a.testVar - b;
        return ret;
    }

    public static LuaTestObj operator -(int a, LuaTestObj b)
    {
        LuaTestObj ret = new LuaTestObj();
        ret.testVar = a - b.testVar;
        return ret;
    }

    public static LuaTestObj operator * (LuaTestObj a, LuaTestObj b)
    {
        LuaTestObj ret = new LuaTestObj();
        ret.testVar = a.testVar * b.testVar;
        return ret;
    }

    public static LuaTestObj operator *(LuaTestObj a, int b)
    {
        LuaTestObj ret = new LuaTestObj();
        ret.testVar = a.testVar * b;
        return ret;
    }

    public static LuaTestObj operator *(int a, LuaTestObj b)
    {
        LuaTestObj ret = new LuaTestObj();
        ret.testVar = a * b.testVar;
        return ret;
    }


    public static LuaTestObj operator /(LuaTestObj a, LuaTestObj b)
    {
        LuaTestObj ret = new LuaTestObj();
        ret.testVar = a.testVar / b.testVar;
        return ret;
    }

    public static LuaTestObj operator /(LuaTestObj a, int b)
    {
        LuaTestObj ret = new LuaTestObj();
        ret.testVar = a.testVar / b;
        return ret;
    }

    public static LuaTestObj operator /(int a, LuaTestObj b)
    {
        LuaTestObj ret = new LuaTestObj();
        ret.testVar = a / b.testVar;
        return ret;
    }

    public static LuaTestObj operator % (LuaTestObj a, LuaTestObj b)
    {
        LuaTestObj ret = new LuaTestObj();
        ret.testVar = a.testVar % b.testVar;
        return ret;
    }

    public static LuaTestObj operator %(int a, LuaTestObj b)
    {
        LuaTestObj ret = new LuaTestObj();
        ret.testVar = a % b.testVar;
        return ret;
    }

    public static LuaTestObj operator %(LuaTestObj a, int b)
    {
        LuaTestObj ret = new LuaTestObj();
        ret.testVar = a.testVar % b;
        return ret;
    }

    public static LuaTestObj operator - (LuaTestObj a)
    {
        LuaTestObj ret = new LuaTestObj();
        ret.testVar = a.testVar * (-1);
        return ret;
    }

    public static bool operator < (LuaTestObj a, LuaTestObj b)
    {
        return (a.testVar < b.testVar);
    }


    public static bool operator <= (LuaTestObj a, LuaTestObj b)
    {
        return (a.testVar <= b.testVar);
    }



    public static bool operator > (LuaTestObj a, LuaTestObj b)
    {
        return (a.testVar > b.testVar);
    }



    public static bool operator >=(LuaTestObj a, LuaTestObj b)
    {
        return (a.testVar >= b.testVar);
    }

   

    public int this[int i]
    {
        get
        {
            return testArr[i];
        }
        set
        {
            testArr[i] = value;
        }
    }  

    
    public event TestEvtHandler1 TestEvent1;
    public event TestEvtHandler2 TestEvent2;

	public static event TestEvtHandler1 TestStaticEvent1;

    public int CallEvent(float y)
    {
        return TestEvent1(y);
    }

	public static int CallStaticEvent(float y)
	{
		return TestStaticEvent1(y);
	}

	public static int DefaultParaFuncSingle(int i, string str = "abc")
	{
		return i;
	}

	public static int DefaultParaFuncMulti(int i, string str = "abc", double d = 0, byte c = 97)
	{
		return (i + 1);
	}

	public static int VariableParamFunc(int i, params string[] strs)
	{
		return 0;
	}
	
	public static int VariableParamFunc2(params int[] strs)
    {
        return strs.Length;
    }
	
	public static int TestEnumFunc(LuaTestType x)
	{
		return (int)x;
	}

	public static string TestGetType(Type x)
	{
		return x.ToString ();
	}

	public static ulong ulX1 = 1;
	public static ulong ulX2 = 1;
	public static long lY1 = 1;
	public static long lY2 = 1;
	public static Int64 i64Z1 = 1;
	public static Int64 i64Z2 = 1;
	public static long lY3 = 1;
	public static long lY4 = 123;
	public static long lY5 = 12345;
	public static long lY6 = 54321;

	public static void Gen64BitInt()
	{
		ulX1 = ulX2 = 1;
		lY1 = lY2 = lY3 = 1;
		i64Z1 = i64Z2 = 1;
		ulX1 = ulX1 << 62;
		ulX2 = ulX2 << 61;
		lY1 = lY1 << 62;
		lY2 = lY2 << 61;
		i64Z1 = i64Z1 << 62;
		i64Z2 = i64Z2 << 61;
		lY3 = lY3 << 50;

	}

	public static ITestLuaClass CreateTestLuaObj()
	{
		return new TestLuaClass ();
	}

    public static TestDelegate csDelegate;
    public static TestDelegate csDelegate1;
    public static TestDelegate csDelegate2;
    public static TestDelegate csDelegate3;
    public static TestDelegate csDelegate4;

    public static TestDelegate csDelegate11;
    public static TestDelegate csDelegate12;
    public static TestDelegate csDelegate13;

	public static int initNumber = 5;

	public static int CalcAdd(int x)
	{
		initNumber += x;
		return initNumber;
	}

	public static int calcadd(int x)
	{
		initNumber += x;
		return initNumber;
	}

	public static int CalcDel(int x)
	{
		initNumber -= x;
		return initNumber;
	}

	public static int CalcMul(int x)
	{
		initNumber *= x;
		return initNumber;
	}

	public static void GenDelegate()
	{
        csDelegate1 = new TestDelegate(CalcAdd);
        csDelegate2 = new TestDelegate(CalcDel);
        csDelegate3 = new TestDelegate(CalcMul);
	}


    public static int OverLoad1(int x, int y)
    {
        return 1;
    }

    public static int OverLoad1(int x)
    {
        return 2;
    }

	public static int OverLoad1(params int[] vars)
	{
		int ret = 0;
		foreach (int var in vars) 
		{ 
			ret += var; 
		} 
		return ret;
	}
	
	public static int OverLoad2(int x, float y)
    {
        return 3;
    }

    public static int OverLoad2(string x, string y)
    {
        return 4;
    }

    public static int OverLoad3(int x)
    {
        return 5;
    }

    public static int OverLoad3(short y)
    {
        return 6;
    }


    public static void OutRefFunc1(int x, out int y, ref int z)
    {
        y = 100;
    }

    public static void OutRefFunc2(ref int x, int y, out int z)
    {
        z = 200;
    }

    public static void OutRefFunc3(int x, out int y, ref int z)
    {
        y = 300;
    }

    public static int OutRefFunc4(int x, out int y, ref int z)
    {
        y = 400;
        return y;
    }

    public static int OutRefFunc5(ref int x, int y, out int z)
    {
        z = 500;
        return z;
    }

    public static int OutRefFunc6(int x, int y)
    {
        return 600;
    }

    public static void OutRefFunc11(ITestLuaClass x, out ITestLuaClass y, ref ITestLuaClass z)
    {
        y = CreateTestLuaObj();
        y.cmpTarget = 100;
    }

    public static void OutRefFunc12(ref ITestLuaClass x, ITestLuaClass y, out ITestLuaClass z)
    {
        z = CreateTestLuaObj();
        z.cmpTarget = 200;
    }

    public static void OutRefFunc13(ITestLuaClass x, out ITestLuaClass y, ref ITestLuaClass z)
    {
        y = CreateTestLuaObj();
        y.cmpTarget = 300;
    }

    public static int OutRefFunc14(ITestLuaClass x, out ITestLuaClass y, ref ITestLuaClass z)
    {
        y = CreateTestLuaObj();
        y.cmpTarget = 400;
        return y.cmpTarget;
    }

    public static int OutRefFunc15(ref ITestLuaClass x, ITestLuaClass y, out ITestLuaClass z)
    {
        z = CreateTestLuaObj();
        z.cmpTarget = 500;
        return z.cmpTarget;
    }

    public static int OutRefFunc16(ITestLuaClass x, ITestLuaClass y)
    {
        return 600;
    }

    public static void OutRefFunc21(TestDelegate x, out TestDelegate y, ref TestDelegate z)
    {
        y = new TestDelegate(CalcAdd);
    }

    public static void OutRefFunc22(ref TestDelegate x, TestDelegate y, out TestDelegate z)
    {
        z = new TestDelegate(CalcAdd);
    }

    public static void OutRefFunc23(TestDelegate x, out TestDelegate y, ref TestDelegate z)
    {
        y = new TestDelegate(CalcAdd);
    }

    public static int OutRefFunc24(TestDelegate x, out TestDelegate y, ref TestDelegate z)
    {
        y = new TestDelegate(CalcAdd);
        return y(1);
    }

    public static int OutRefFunc25(ref TestDelegate x, TestDelegate y, out TestDelegate z)
    {
        z = new TestDelegate(CalcAdd);
        return z(1);
    }

    public static int OutRefFunc26(TestDelegate x, TestDelegate y)
    {
        return 600;
    }

	public int Sum(int a, int b, int c)
	{
		return a + b + c;
	}

	public static int Sum(int a, int b)
	{
		return a + b;
	}

	public void GenericMethod<T>()
	{
		LuaTestCommon.Log("GenericMethod<" + typeof(T) +">");
	}

    public IntPtr ptr = System.Runtime.InteropServices.Marshal.AllocHGlobal(100);

    public IntPtr GetPtr()
    {
        byte[] abc = new byte[] { 97, 98, 99 };
        System.Runtime.InteropServices.Marshal.Copy(abc, 0, ptr, abc.Length);
        return ptr;
    }

    public byte PrintPtr(IntPtr p)
    {
        LuaTestCommon.Log("p == ptr?" + (p == ptr));
        byte[] abc = new byte[] { 0, 0, 0 };
        System.Runtime.InteropServices.Marshal.Copy(p, abc, 0, abc.Length);
        LuaTestCommon.Log(string.Format("{0},{1},{2}", abc[0], abc[1], abc[2]));
        return abc[0];
    }

    public int VariableParamFuncDefault(int d, int i = 1, params string[] strs)
    {
        return i + d;
    }

    public static double StaticVariableParamFuncDefault(double d, double i = 1.0, params string[] strs)
    {
        return i + d;
    }
	
	public static byte[] FuncReturnByteArray()
	{
		byte[] abc = new byte[] { 97, 98, 99 };
		return abc;
	}

    public static byte FuncReturnByte()
    {
        byte abc = 97;
        return abc;
    }
	
	public static int[] FuncReturnIntArray()
	{
		int[] abc = new int[] { 97, 98, 99 };
		return abc;
	}

    public static int FuncReturnInt()
    {
        int abc = 97;
        return abc;
    }

#if !XLUA_GENERAL
    public static LayerMask TestImplicit()
    {
        return new LayerMask();
    }
#endif

    public static int VariableParamFunc(int i, params int[] strs)
    {
        return 0;
    }
	
	public static string FirstPushEnumFunc(int i)
	{
        string luaScript = @"
        function first_push(t,obj)
	        if t==1 then
		        if obj == CS.FirstPushEnum.E1 then
			        return 1
		        else
			        return 2
		        end
	        elseif t==2 then
		        if obj == CS.FirstPushEnum.self then
			        return 3
		        else
			        return 4
		        end
	        else
		        return 5
	        end
        end";
        luaEnv.DoString(luaScript);
		LuaFunction f1 = luaEnv.Global.Get<LuaFunction>("first_push");
        LuaTestCommon.Log("LuaFunction<" + f1);
        object[] ret = f1.Call(i, FirstPushEnum.E1);
        return ret[0].ToString();
	}
}

[LuaCallCSharp]
public class TestCastClass
{
	public bool TestFunc1()
	{
		return true;

	}
}

[LuaCallCSharp]
public interface ITestLuaClass
{
	bool TestFunc1();
    int cmpTarget{ set; get; }
}

internal class TestLuaClass : ITestLuaClass
{
	public bool TestFunc1()
	{
		return true;
	}
    public int cmpTarget { set; get; }
}

[LuaCallCSharp]
public class  TestChineseString
{

	public string GetShortChinString()
	{
		return short_simple_string;
	}

	public string GetLongChineString()
	{
		return long_simple_string;
	}

	public string GetCombineString()
	{
		return combine_string;
	}

	public string GetComplexString()
	{
		return complex_string;
	}

	public string GetHuoxingString()
	{
		return huoxing_string;
	}

	public string short_simple_string = "中文字符串";
	public string long_simple_string = "为Unity3D增加Lua脚本编程的能力，进而提供代码逻辑增量更新的可能，支持lua的所有基本类型，哈哈哈哈";
	public string combine_string = "中文字符串.* ? [ ] ^ $~`!@#$%^&()_-+=[];',““〈〉〖【℃ ＄ ¤№ ☆ ★■ⅷ②㈣12345abc";
	public string complex_string = "繁體國陸";
	public string huoxing_string = "吙煋呅僦媞這樣孒";
}

public class TestUlongAndLongType
{
	public ulong UlongMax
	{

		get { return ulong_max; }
		set { ulong_max=value; }
	}

	public ulong UlongMin
	{
		
		get { return ulong_min; }
		set { ulong_min=value; }
	}

	public ulong UlongMid
	{
		
		get { return ulong_mid; }
		set { ulong_mid=value; }
	}

	public ulong UlongAdd()
	{
		return ulong_max - 1000 + 1;
	}

	public long LongMax
	{
		
		get { return long_max; }
		set { long_max=value; }
	}
	
	public long LongMin
	{
		
		get { return long_min; }
		set { long_min=value; }
	}
	
	public long LongMid
	{
		
		get { return long_mid; }
		set { long_mid=value; }
	}

	public long LongAdd()
	{
		return long_max - 1000 + 1;
	}

	ulong ulong_max = System.UInt64.MaxValue;
	ulong ulong_min = System.UInt64.MinValue;
	ulong ulong_mid = 9223372036854775808;
	long long_max = System.Int64.MaxValue;
	long long_min = System.Int64.MinValue;
	long long_mid = 4611686018427387904;
}

[LuaCallCSharp]
public struct Employeestruct
{
	private string name;
	public string Name
	{
		get { return name; }
		set { name = value; }
	}
	
	private int age;
	public int Age
	{
		get { return age; }
		set { age = value; }
	}
	
	private int salary;
	private int annual_bonus;

	public int Salary
	{
		get { return salary; }
		set { salary = value; }
	}

	public int AnnualBonus
	{
		get { return annual_bonus; }
		set { annual_bonus = value; }
	}

}


public struct ConStruct{
	public int x;
	public int y;
	public string z;
	public ConStruct(int x, int y, string z)
	{
		this.x = x;
		this.y = y;
		this.z = z;
	}
}

[GCOptimize]
[LuaCallCSharp]
public struct StaticPusherStructA
{
	public byte byteVar;
	public sbyte sbyteVar;
	public StaticPusherStructA(byte a, sbyte b)
	{
		this.byteVar = a;
		this.sbyteVar = b;
	}
}


[GCOptimize]
[LuaCallCSharp]
public struct StaticPusherStructB
{
	public short shortVar;
	public ushort ushortVar;
	public int intVar;
	public uint uintVar;
	public StaticPusherStructB(short a, ushort b, int c, uint d)
	{
		this.shortVar = a;
		this.ushortVar = b;
		this.intVar = c;
		this.uintVar = d;
	}
}

// Tencent/xLua#1186 and #1141: bool, char, IntPtr and UIntPtr fields. A
// [GCOptimize] struct with one of them was boxed (Generator.SizeOf returned
// -1); generated code now copies it like the other GCOptimize structs.
[GCOptimize]
[LuaCallCSharp]
public struct StaticPusherStructFlags
{
	public bool boolVar;
	public char charVar;
	public IntPtr ptrVar;
	public UIntPtr uptrVar;

	public static StaticPusherStructFlags Make(bool b, int c, long p)
	{
		return new StaticPusherStructFlags { boolVar = b, charVar = (char)c, ptrVar = new IntPtr(p), uptrVar = new UIntPtr((ulong)(p + 1)) };
	}

	// A double, so that LuaJIT gets a number and not an int64 userdata.
	public static double Ptr(StaticPusherStructFlags v)
	{
		return v.ptrVar.ToInt64() + (double)v.uptrVar.ToUInt64();
	}

	public static string Describe(StaticPusherStructFlags v)
	{
		return v.boolVar + "," + v.charVar + "," + v.ptrVar.ToInt64() + "," + v.uptrVar.ToUInt64();
	}

	// Managed bytes allocated on this thread while f(n) runs, after a warm-up
	// call, or -1 when the runtime cannot tell. A double, as Ptr.
	public static double Allocated(LuaFunction f, int n)
	{
		var counter = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", Type.EmptyTypes);
		if (counter == null) return -1;
		f.Call(10);
		long before = (long)counter.Invoke(null, null);
		f.Call(n);
		return (long)counter.Invoke(null, null) - before;
	}
}

[GCOptimize]
[LuaCallCSharp]
public struct StaticPusherStructAll
{
	public long longVar;
	public ulong ulongVar;
	public float floatVar;
	public double doubleVar;
	public StaticPusherStructA structA;
	public StaticPusherStructB structB;
}

public interface ITest
{
	int Add(int x,int y);
}

public struct NoGenCodeStruct:ITest{

	private byte byte_var;
	private short short_var;
	private ushort ushort_var;
	private int int_var1;
	private int int_var2;
	private uint uint_var;
	private long long_var;
	private ulong ulong_var;
	private double double_var;
	private float float_var;
	private char char_var;
	private decimal decimal_var;
	private string string_var;

	public byte Byte
	{
		get { return byte_var; }
		set { byte_var = value; }
	}

	public short Short
	{
		get { return short_var; }
		set { short_var = value; }
	}

	public ushort UShort
	{
		get { return ushort_var; }
		set { ushort_var = value; }
	}

	public int IntVar1
	{
		get { return int_var1; }
		set { int_var1 = value; }
	}

	public int IntVar2
	{
		get { return int_var2; }
		set { int_var2 = value; }
	}

	public uint UInt
	{
		get { return uint_var; }
		set { uint_var = value; }
	}

	public long Long
	{
		get { return long_var; }
		set { long_var = value; }
	}

	public ulong ULong
	{
		get { return ulong_var; }
		set { ulong_var = value; }
	}

	public double Double
	{
		get { return double_var; }
		set { double_var = value; }
	}

	public float Float
	{
		get { return float_var; }
		set { float_var = value; }
	}

	public char Char
	{
		get { return char_var; }
		set { char_var = value; }
	}
	
	public decimal Decimal
	{
		get { return decimal_var; }
		set { decimal_var = value; }
	}
	
	public string String
	{
		get { return string_var; }
		set { string_var = value; }
	}

	public int Add(int x, int y)
	{
		return x + y;
	}

	private ConStruct con;

	public ConStruct IncludeStruct
	{
		get { return con; }
		set { con = value; }
	}
}

public class NoGenCodeBaseClass
{
	public NoGenCodeStruct InitStruct(out int add){
		struct_var.Byte = 10;
		struct_var.Char = 'a';
		struct_var.Decimal = 12.33333333m;
		struct_var.Double = 10.111111111111;
		struct_var.Float = 11.1234f;
		struct_var.IntVar1 = 12345678;
		struct_var.IntVar2 = -12345678;
		struct_var.Long = System.Int64.MaxValue;
		struct_var.ULong = System.UInt64.MaxValue;
		struct_var.Short = 123;
		struct_var.String = "just for test";
		struct_var.UInt = 12345;
		struct_var.UShort = 255;
		struct_var.IncludeStruct = new ConStruct (1, 2, "haha");
		add = struct_var.Add (struct_var.IntVar1, struct_var.IntVar2);
		return struct_var;
	}

	public void SetStruct(ref NoGenCodeStruct var, out NoGenCodeStruct value)
	{
		struct_var.Byte = var.Byte;
		struct_var.Char = var.Char;
		struct_var.Decimal = var.Decimal;
		struct_var.Double = var.Double;
		struct_var.Float = var.Float;
		struct_var.IntVar1 = var.IntVar1;
		struct_var.IntVar2 = var.IntVar2;
		struct_var.Long = var.Long;
		struct_var.ULong = var.ULong;
		struct_var.Short = var.Short;
		struct_var.String = var.String;
		struct_var.UInt = var.UInt;
		struct_var.UShort = var.UShort;
		struct_var.IncludeStruct = new ConStruct(var.IncludeStruct.x, var.IncludeStruct.y, var.IncludeStruct.z);
		value = struct_var;
	}

	public NoGenCodeStruct GetStruct(){
		return struct_var;
	}

	public void SetStruct(GenCodeStruct var, out int add, out NoGenCodeStruct value)
	{
		struct_var.Byte = var.Byte;
		struct_var.Char = var.Char;
		struct_var.Decimal = var.Decimal;
		struct_var.Double = var.Double;
		struct_var.Float = var.Float;
		struct_var.IntVar1 = var.IntVar1;
		struct_var.IntVar2 = var.IntVar2;
		struct_var.Long = var.Long;
		struct_var.ULong = var.ULong;
		struct_var.Short = var.Short;
		struct_var.String = var.String;
		struct_var.UInt = var.UInt;
		struct_var.UShort = var.UShort;
		struct_var.IncludeStruct = new ConStruct(var.IncludeStruct.x, var.IncludeStruct.y, var.IncludeStruct.z);
		add = struct_var.Add (var.IntVar1, var.IntVar2);
		value = struct_var;
		LuaTestCommon.Log ("NoGenCodeStruct Byte:" + struct_var.Byte);
		LuaTestCommon.Log ("NoGenCodeStruct Char:" + struct_var.Char);
		LuaTestCommon.Log ("NoGenCodeStruct Decimal:" + struct_var.Decimal);
		LuaTestCommon.Log ("NoGenCodeStruct Double:" + struct_var.Double);
		LuaTestCommon.Log ("NoGenCodeStruct Float:" + struct_var.Float);
		LuaTestCommon.Log ("NoGenCodeStruct IntVar1:" + struct_var.IntVar1);
		LuaTestCommon.Log ("NoGenCodeStruct IntVar2:" + struct_var.IntVar2);
		LuaTestCommon.Log ("NoGenCodeStruct Long:" + struct_var.Long);
		LuaTestCommon.Log ("NoGenCodeStruct ULong:" + struct_var.ULong);
		LuaTestCommon.Log ("NoGenCodeStruct Short:" + struct_var.Short);
		LuaTestCommon.Log ("NoGenCodeStruct String:" + struct_var.String);
		LuaTestCommon.Log ("NoGenCodeStruct UInt:" + struct_var.UInt);
		LuaTestCommon.Log ("NoGenCodeStruct UShort:" + struct_var.UShort);
		LuaTestCommon.Log ("NoGenCodeStruct IncludeStruct.x:" + struct_var.IncludeStruct.x + ", IncludeStruct.y:" 
		           + struct_var.IncludeStruct.y + ", IncludeStruct.z:" + struct_var.IncludeStruct.z);
	}

	public static NoGenCodeStruct GetStaticVar()
	{
		LuaTestCommon.Log ("static struct byte:" + struct_var1.Byte);
		return struct_var1;
	}

	public void SetStaticPusherStruct(StaticPusherStructAll inVar, ref StaticPusherStructAll refVar, out StaticPusherStructAll outVar)
	{
		static_pushstruct_var.longVar = inVar.longVar + refVar.longVar;
		static_pushstruct_var.ulongVar = inVar.ulongVar + refVar.ulongVar;
		static_pushstruct_var.floatVar = inVar.floatVar + refVar.floatVar;
		static_pushstruct_var.doubleVar = inVar.doubleVar + refVar.doubleVar;
		static_pushstruct_var.structA.byteVar = 10;
		static_pushstruct_var.structA.sbyteVar = 100;
		static_pushstruct_var.structB.intVar = inVar.structB.intVar + refVar.structB.intVar;
		static_pushstruct_var.structB.uintVar = inVar.structB.uintVar + refVar.structB.uintVar;
		static_pushstruct_var.structB.shortVar = refVar.structB.shortVar;
		static_pushstruct_var.structB.ushortVar = inVar.structB.ushortVar;
		outVar = static_pushstruct_var;
		refVar.longVar = refVar.longVar - 1;
		refVar.ulongVar = refVar.ulongVar - 1;
		refVar.floatVar = refVar.floatVar - 1.0f;
		refVar.doubleVar = refVar.doubleVar - 1.0;
		refVar.structA.byteVar = 98;
		refVar.structA.sbyteVar = 101;
		refVar.structB.intVar = refVar.structB.intVar + 1;
		refVar.structB.uintVar = refVar.structB.uintVar + 1;
		refVar.structB.shortVar = inVar.structB.shortVar;
		refVar.structB.ushortVar = inVar.structB.ushortVar;
	}

	private NoGenCodeStruct struct_var;
	public static NoGenCodeStruct struct_var1;
	public static ConStruct struct_var2 = new ConStruct (3, 4, "enen");
	public static int GBS = 1;
	public StaticPusherStructAll static_pushstruct_var;
}

public class NoGenCodeDrivedClass:NoGenCodeBaseClass
{
	public ConStruct SimpleConStruct
	{
		get { return simple_struct; }
		set { simple_struct = value; }
	}

	public int Add(int x, int y)
	{
		return x + y;
	}

	public void Add(ConStruct invar, ref ConStruct refvar, out ConStruct outvar)
	{
		outvar = invar;
		outvar.x = invar.x + refvar.x;
		refvar.x = invar.x;
	}

	private ConStruct simple_struct;
}


[LuaCallCSharp]
public struct HasConstructStruct{
	public int x;
	public int y;
	public string z;
	public HasConstructStruct(int x, int y, string z)
	{
		this.x = x;
		this.y = y;
		this.z = z;
	}
}

[LuaCallCSharp]
public interface IGenCodeTest
{
	int Add(int x,int y);
}

[LuaCallCSharp]
public struct GenCodeStruct:IGenCodeTest{
	
	private byte byte_var;
	private short short_var;
	private ushort ushort_var;
	private int int_var1;
	private int int_var2;
	private uint uint_var;
	private long long_var;
	private ulong ulong_var;
	private double double_var;
	private float float_var;
	private char char_var;
	private decimal decimal_var;
	private string string_var;
	
	public byte Byte
	{
		get { return byte_var; }
		set { byte_var = value; }
	}
	
	public short Short
	{
		get { return short_var; }
		set { short_var = value; }
	}
	
	public ushort UShort
	{
		get { return ushort_var; }
		set { ushort_var = value; }
	}
	
	public int IntVar1
	{
		get { return int_var1; }
		set { int_var1 = value; }
	}
	
	public int IntVar2
	{
		get { return int_var2; }
		set { int_var2 = value; }
	}
	
	public uint UInt
	{
		get { return uint_var; }
		set { uint_var = value; }
	}
	
	public long Long
	{
		get { return long_var; }
		set { long_var = value; }
	}
	
	public ulong ULong
	{
		get { return ulong_var; }
		set { ulong_var = value; }
	}
	
	public double Double
	{
		get { return double_var; }
		set { double_var = value; }
	}
	
	public float Float
	{
		get { return float_var; }
		set { float_var = value; }
	}
	
	public char Char
	{
		get { return char_var; }
		set { char_var = value; }
	}
	
	public decimal Decimal
	{
		get { return decimal_var; }
		set { decimal_var = value; }
	}
	
	public string String
	{
		get { return string_var; }
		set { string_var = value; }
	}
	
	public int Add(int x, int y)
	{
		return x + y;
	}
	
	private HasConstructStruct con;
	
	public HasConstructStruct IncludeStruct
	{
		get { return con; }
		set { con = value; }
	}
}

[LuaCallCSharp]
public class GenCodeBaseClass
{
	public GenCodeStruct InitStruct(out int add){
		struct_var.Byte = 100;
		struct_var.Char = 'b';
		struct_var.Decimal = 22.33333333m;
		struct_var.Double = 20.111111111111;
		struct_var.Float = 21.1234f;
		struct_var.IntVar1 = 22345678;
		struct_var.IntVar2 = -22345678;
		struct_var.Long = System.Int64.MinValue;
		struct_var.ULong = System.UInt64.MinValue;
		struct_var.Short = 223;
		struct_var.String = "2just for test";
		struct_var.UInt = 22345;
		struct_var.UShort = 128;
		struct_var.IncludeStruct = new HasConstructStruct (2, 2, "2haha");
		add = struct_var.Add (struct_var.IntVar1, struct_var.IntVar2);
		return struct_var;
	}

	public void SetStruct(ref GenCodeStruct var, out GenCodeStruct value)
	{
		struct_var.Byte = var.Byte;
		struct_var.Char = var.Char;
		struct_var.Decimal = var.Decimal;
		struct_var.Double = var.Double;
		struct_var.Float = var.Float;
		struct_var.IntVar1 = var.IntVar1;
		struct_var.IntVar2 = var.IntVar2;
		struct_var.Long = var.Long;
		struct_var.ULong = var.ULong;
		struct_var.Short = var.Short;
		struct_var.String = var.String;
		struct_var.UInt = var.UInt;
		struct_var.UShort = var.UShort;
		struct_var.IncludeStruct = new HasConstructStruct(var.IncludeStruct.x, var.IncludeStruct.y, var.IncludeStruct.z);
		value = struct_var;
	}
	
	public GenCodeStruct GetStruct(){
		return struct_var;
	}
	
	public void SetStruct(NoGenCodeStruct var, out int add, out GenCodeStruct value)
	{
		struct_var.Byte = var.Byte;
		struct_var.Char = var.Char;
		struct_var.Decimal = var.Decimal;
		struct_var.Double = var.Double;
		struct_var.Float = var.Float;
		struct_var.IntVar1 = var.IntVar1;
		struct_var.IntVar2 = var.IntVar2;
		struct_var.Long = var.Long;
		struct_var.ULong = var.ULong;
		struct_var.Short = var.Short;
		struct_var.String = var.String;
		struct_var.UInt = var.UInt;
		struct_var.UShort = var.UShort;
		struct_var.IncludeStruct = new HasConstructStruct(var.IncludeStruct.x, var.IncludeStruct.y, var.IncludeStruct.z);
		add = struct_var.Add (var.IntVar1, var.IntVar2);
		value = struct_var;
		LuaTestCommon.Log ("GenCodeStruct Byte:" + struct_var.Byte);
		LuaTestCommon.Log ("GenCodeStruct Char:" + struct_var.Char);
		LuaTestCommon.Log ("GenCodeStruct Decimal:" + struct_var.Decimal);
		LuaTestCommon.Log ("GenCodeStruct Double:" + struct_var.Double);
		LuaTestCommon.Log ("GenCodeStruct Float:" + struct_var.Float);
		LuaTestCommon.Log ("GenCodeStruct IntVar1:" + struct_var.IntVar1);
		LuaTestCommon.Log ("GenCodeStruct IntVar2:" + struct_var.IntVar2);
		LuaTestCommon.Log ("GenCodeStruct Long:" + struct_var.Long);
		LuaTestCommon.Log ("GenCodeStruct ULong:" + struct_var.ULong);
		LuaTestCommon.Log ("GenCodeStruct Short:" + struct_var.Short);
		LuaTestCommon.Log ("GenCodeStruct String:" + struct_var.String);
		LuaTestCommon.Log ("GenCodeStruct UInt:" + struct_var.UInt);
		LuaTestCommon.Log ("GenCodeStruct UShort:" + struct_var.UShort);
		LuaTestCommon.Log ("GenCodeStruct IncludeStruct.x:" + struct_var.IncludeStruct.x + ", IncludeStruct.y:" 
		           + struct_var.IncludeStruct.y + ", IncludeStruct.z:" + struct_var.IncludeStruct.z);
	}

    public void SetStaticPusherStruct(StaticPusherStructAll inVar, ref StaticPusherStructAll refVar, out StaticPusherStructAll outVar)
    {
        static_pushstruct_var.longVar = inVar.longVar + refVar.longVar;
        static_pushstruct_var.ulongVar = inVar.ulongVar + refVar.ulongVar;
        static_pushstruct_var.floatVar = inVar.floatVar + refVar.floatVar;
        static_pushstruct_var.doubleVar = inVar.doubleVar + refVar.doubleVar;
        static_pushstruct_var.structA.byteVar = 97;
        static_pushstruct_var.structA.sbyteVar = 100;
        static_pushstruct_var.structB.intVar = inVar.structB.intVar + refVar.structB.intVar;
        static_pushstruct_var.structB.uintVar = inVar.structB.uintVar + refVar.structB.uintVar;
        static_pushstruct_var.structB.shortVar = refVar.structB.shortVar;
        static_pushstruct_var.structB.ushortVar = inVar.structB.ushortVar;
        outVar = static_pushstruct_var;
        refVar.longVar = refVar.longVar - 1;
        refVar.ulongVar = refVar.ulongVar - 1;
        refVar.floatVar = refVar.floatVar - 1.0f;
        refVar.doubleVar = refVar.doubleVar - 1.0;
        refVar.structA.byteVar = 98;
        refVar.structA.sbyteVar = 101;
        refVar.structB.intVar = refVar.structB.intVar + 1;
        refVar.structB.uintVar = refVar.structB.uintVar + 1;
        refVar.structB.shortVar = inVar.structB.shortVar;
        refVar.structB.ushortVar = inVar.structB.ushortVar;
    }
	
	private GenCodeStruct struct_var;
    public StaticPusherStructAll static_pushstruct_var;
	public static GenCodeStruct struct_var1;
	public static HasConstructStruct struct_var2 = new HasConstructStruct (4, 5, "enen");
	public static int GBS = 1;
}

[LuaCallCSharp]
public class GenCodeDrivedClass:GenCodeBaseClass
{
	public HasConstructStruct SimpleConStruct
	{
		get { return simple_struct; }
		set { simple_struct = value; }
	}
	
	public int Add(int x, int y)
	{
		return x + y;
	}
	
	public void Add(HasConstructStruct invar, ref HasConstructStruct refvar, out HasConstructStruct outvar)
	{
		outvar = invar;
		outvar.x = invar.x + refvar.x;
		refvar.x = invar.x;
	}
	
	private HasConstructStruct simple_struct;
}

[LuaCallCSharp]
public class CClass
{
	public CClass(int x, int y, string z)
	{
		c_struct = new HasConstructStruct(x, y, z);
	}

	public HasConstructStruct CConStruct
	{
		get { return c_struct; }
		set { c_struct = value; }
	}
	
	public void Add(HasConstructStruct invar, ref HasConstructStruct refvar, out HasConstructStruct outvar)
	{
		outvar = invar;
		outvar.x = invar.x + refvar.x;
		refvar.x = invar.x;
	}

	public int VariableParamFunc(params HasConstructStruct[] structs)
	{
		int ret = 0;
		foreach (HasConstructStruct var in structs) 
		{ 
			ret += var.x; 
		} 
		return ret;
	}
	
	private HasConstructStruct c_struct;
}

public class BClass:CClass
{
	public BClass(int x, int y, string z):base(x, y, z)
	{
		b_struct = new HasConstructStruct(x, y, z);
	}

	public HasConstructStruct BConStruct
	{
		get { return b_struct; }
		set { b_struct = value; }
	}
	
	public void Sub(HasConstructStruct invar, ref HasConstructStruct refvar, out HasConstructStruct outvar)
	{
		outvar = invar;
		outvar.x = invar.x - refvar.x;
		refvar.x = invar.x;
		LuaTestCommon.Log ("refvar.x:" + refvar.x + ",refvar.y:" + refvar.y + ", refvar.z:" + refvar.z);
	}
	
	private HasConstructStruct b_struct;
}

[LuaCallCSharp]
public class AClass:BClass
{
	public AClass(int x, int y , string z):base(x, y, z)
	{
		LuaTestCommon.Log ("AClass Constructor");
	}

    public int Div(int x, int y)
    {
		if (y == 0) {
			return 0;
		}
		else 
		{
			return x / y;
		}
	}
}

public struct NoGen2FloatStruct{
	public float a;
	public float b;
	public NoGen2FloatStruct(float a, float b)
	{
		this.a = a;
		this.b = b;
	}
}

public struct NoGen3FloatStruct{
	public float a;
	public float b;
	public float c;
	public NoGen3FloatStruct(float a, float b, float c)
	{
		this.a = a;
		this.b = b;
		this.c = c;
	}
}

public struct NoGen4FloatStruct{
	public float a;
	public float b;
	public float c;
	public float d;
	public NoGen4FloatStruct(float a, float b, float c, float d)
	{
		this.a = a;
		this.b = b;
		this.c = c;
		this.d = d;
	}
}

public struct NoGen5FloatStruct{
	public float a;
	public float b;
	public float c;
	public float d;
	public float e;
	public NoGen5FloatStruct(float a, float b, float c, float d, float e)
	{
		this.a = a;
		this.b = b;
		this.c = c;
		this.d = d;
		this.e = e;
	}
}

public struct NoGen6FloatStruct{
	public float a;
	public float b;
	public float c;
	public float d;
	public float e;
	public float f;
	public NoGen6FloatStruct(float a, float b, float c, float d, float e, float f)
	{
		this.a = a;
		this.b = b;
		this.c = c;
		this.d = d;
		this.e = e;
		this.f = f;
	}
}

public class TestNoGenFloatStructClass {

	public NoGen2FloatStruct Struct2{
		set { struct_2 = value; }
		get { return struct_2; }
	}

	public NoGen3FloatStruct Struct3{
		set { struct_3 = value; }
		get { return struct_3; }
	}

	public NoGen4FloatStruct Struct4{
		set { struct_4 = value; }
		get { return struct_4; }
	}

	public NoGen5FloatStruct Struct5{
		set { struct_5 = value; }
		get { return struct_5; }
	}

	public NoGen6FloatStruct Struct6{
		set { struct_6 = value; }
		get { return struct_6; }
	}

	public void Add2(ref NoGen2FloatStruct x, ref Gen2FloatStruct y)
	{
		x.a = x.a + y.a;
		x.b = x.b + y.b;
		y.a = x.a;
		y.b = x.b;
	}

	public void Sub3(ref NoGen3FloatStruct x, ref Gen3FloatStruct y)
	{
		x.a = x.a - y.a;
		x.b = x.b - y.b;
		x.c = x.c - y.c;
		y.a = x.a;
		y.b = x.b;
		y.a = x.c;
	}

	public void Multiply4(ref NoGen4FloatStruct x, ref Gen4FloatStruct y)
	{
		x.a = x.a * y.a;
		x.b = x.b + y.b;
		x.c = x.c - y.c;
		x.d = x.d * y.d;
		y.a = x.a;
		y.b = x.b;
		y.c = x.c;
		y.d = x.d;
	}

	public void All5(ref NoGen5FloatStruct x, ref Gen5FloatStruct y)
	{
		x.a = x.a * y.a;
		x.b = x.b + y.b;
		x.c = x.c - y.c;
		x.d = x.d / y.d;
		x.e = y.e + x.e;
		y.a = x.a;
		y.b = x.b;
		y.c = x.c;
		y.d = x.d;
		y.e = x.e;
	}

	public void All6(ref NoGen6FloatStruct x, ref Gen6FloatStruct y)
	{
		x.a = x.a * y.a;
		x.b = x.b + y.b;
		x.c = x.c - y.c;
		x.d = x.d / y.d;
		x.e = y.e + x.e;
		x.f = x.f - y.f;
		y.a = x.a;
		y.b = x.b;
		y.c = x.c;
		y.d = x.d;
		y.e = x.e;
		y.f = x.f;
	}

	private NoGen2FloatStruct struct_2;
	private NoGen3FloatStruct struct_3;
	private NoGen4FloatStruct struct_4;
	private NoGen5FloatStruct struct_5;
	private NoGen6FloatStruct struct_6; 
}

[GCOptimize]
[LuaCallCSharp]
public struct Gen2FloatStruct{
	public float a;
	public float b;
	public Gen2FloatStruct(float a, float b)
	{
		this.a = a;
		this.b = b;
	}
}

[GCOptimize]
[LuaCallCSharp]
public struct Gen3FloatStruct{
	public float a;
	public float b;
	public float c;
	public Gen3FloatStruct(float a, float b, float c)
	{
		this.a = a;
		this.b = b;
		this.c = c;
	}
}

[GCOptimize]
[LuaCallCSharp]
public struct Gen4FloatStruct{
	public float a;
	public float b;
	public float c;
	public float d;
	public Gen4FloatStruct(float a, float b, float c, float d)
	{
		this.a = a;
		this.b = b;
		this.c = c;
		this.d = d;
	}
}

[GCOptimize]
[LuaCallCSharp]
public struct Gen5FloatStruct{
	public float a;
	public float b;
	public float c;
	public float d;
	public float e;
	public Gen5FloatStruct(float a, float b, float c, float d, float e)
	{
		this.a = a;
		this.b = b;
		this.c = c;
		this.d = d;
		this.e = e;
	}
}

[GCOptimize]
[LuaCallCSharp]
public struct Gen6FloatStruct{
	public float a;
	public float b;
	public float c;
	public float d;
	public float e;
	public float f;
	public Gen6FloatStruct(float a, float b, float c, float d, float e, float f)
	{
		this.a = a;
		this.b = b;
		this.c = c;
		this.d = d;
		this.e = e;
		this.f = f;
	}
}

[LuaCallCSharp]
public class TestGenFloatStructClass {
	
	public Gen2FloatStruct Struct2{
		set { struct_2 = value; }
		get { return struct_2; }
	}
	
	public Gen3FloatStruct Struct3{
		set { struct_3 = value; }
		get { return struct_3; }
	}
	
	public Gen4FloatStruct Struct4{
		set { struct_4 = value; }
		get { return struct_4; }
	}
	
	public Gen5FloatStruct Struct5{
		set { struct_5 = value; }
		get { return struct_5; }
	}
	
	public Gen6FloatStruct Struct6{
		set { struct_6 = value; }
		get { return struct_6; }
	}
	
	public void Add2(ref NoGen2FloatStruct x, ref Gen2FloatStruct y)
	{
		x.a = x.a + y.a;
		x.b = x.b + y.b;
		y.a = x.a;
		y.b = x.b;
	}
	
	public void Sub3(ref NoGen3FloatStruct x, ref Gen3FloatStruct y)
	{
		x.a = x.a - y.a;
		x.b = x.b - y.b;
		x.c = x.c - y.c;
		y.a = x.a;
		y.b = x.b;
		y.a = x.c;
	}
	
	public void Multiply4(ref NoGen4FloatStruct x, ref Gen4FloatStruct y)
	{
		x.a = x.a * y.a;
		x.b = x.b + y.b;
		x.c = x.c - y.c;
		x.d = x.d * y.d;
		y.a = x.a;
		y.b = x.b;
		y.c = x.c;
		y.d = x.d;
	}
	
	public void All5(ref NoGen5FloatStruct x, ref Gen5FloatStruct y)
	{
		x.a = x.a * y.a;
		x.b = x.b + y.b;
		x.c = x.c - y.c;
		x.d = x.d / y.d;
		x.e = y.e + x.e;
		y.a = x.a;
		y.b = x.b;
		y.c = x.c;
		y.d = x.d;
		y.e = x.e;
	}
	
	public void All6(ref NoGen6FloatStruct x, ref Gen6FloatStruct y)
	{
		x.a = x.a * y.a;
		x.b = x.b + y.b;
		x.c = x.c - y.c;
		x.d = x.d / y.d;
		x.e = y.e + x.e;
		x.f = x.f - y.f;
		y.a = x.a;
		y.b = x.b;
		y.c = x.c;
		y.d = x.d;
		y.e = x.e;
		y.f = x.f;
	}
	
	private Gen2FloatStruct struct_2;
	private Gen3FloatStruct struct_3;
	private Gen4FloatStruct struct_4;
	private Gen5FloatStruct struct_5;
	private Gen6FloatStruct struct_6; 
}

public struct NoGen2IntStruct{
	public int a;
	public int b;
	public NoGen2IntStruct(int a, int b)
	{
		this.a = a;
		this.b = b;
	}
}
[CSharpCallLua]
public delegate int TestReflectEvtHandler1(float y);
public class TestReflectEventClass{

	public event TestReflectEvtHandler1 TestEvent1;
	
	public int CallEvent(float y)
	{
		return TestEvent1(y);
	}
}

namespace TestExtensionMethod
{
	[LuaCallCSharp]
	public static class TestExtensionMethodForStruct
	{
		public static void PrintSalary(this Employeestruct i)
		{
			LuaTestCommon.Log("Salary:" + i.Salary);
		}
		
		public static int GetIncomeForOneYear(this Employeestruct i)
		{
			return i.Salary * 12 + i.AnnualBonus;
		}
		
		public static int Add(this Employeestruct i, Employeestruct d)
		{
			return i.Salary * 12 + i.AnnualBonus + d.Salary * 12 + d.AnnualBonus;
		}
		
		public static void Sub(this Employeestruct i, Employeestruct d, out Employeestruct e, ref Employeestruct a)
		{
			e = d;
			e.Salary = 10000;
			a.Salary = i.Salary - d.Salary;
			LuaTestCommon.Log ("e.name:" + e.Name +", e.salary:"+ e.Salary);
			LuaTestCommon.Log ("a.name:" + a.Name +", a.salary:"+ a.Salary);


		}
	}

	[LuaCallCSharp]
	public static class TestExtensionMethodFOrClass
	{
		public static void PrintAllString(this TestChineseString i)
		{
			LuaTestCommon.Log("GetLongChineString:" + i.GetLongChineString());
		}

		public static int GetLongStringLength(this TestChineseString i)
		{
			return i.GetLongChineString ().Length;
		}

		public static int Add(this TestChineseString i, TestChineseString d)
		{
			return i.GetLongChineString ().Length + d.GetLongChineString ().Length;
		}

		public static void Replace(this TestChineseString i , TestChineseString d, out TestChineseString e, ref TestChineseString a)
		{
			e = i;
			a.combine_string = i.short_simple_string;
			a.short_simple_string = i.short_simple_string + d.short_simple_string;
		}
	}
}

[LuaCallCSharp]
public abstract class EmployeeTemplate
{
	public void GetSalary()
	{
		GetBasicSalary();
		AddBonus();
	}

	public abstract int GetBasicSalary();
	public abstract int AddBonus();
}

[LuaCallCSharp]
public class Manager : EmployeeTemplate
{
	public override int GetBasicSalary()
	{
		//Console.WriteLine("Get Manager Basic Salary");
		return 1;
	}
	
	public override int AddBonus()
	{
		//Console.WriteLine("Add Manager Bonus");
		return 2;
	}
}

[GCOptimize]
[LuaCallCSharp]
public class TableAutoTransSimpleClass
{
    public TableAutoTransSimpleClass()
    {
    }
    public TableAutoTransSimpleClass(int x, string y, long z)
    {
        IntVar = x;
        StringVar = y;
        LongVar = z;
    }
    public int IntVar{
    
        get { return x; }
		
        set { x = value; }
    }

    public string StringVar
    {

        get { return y; }

        set { y = value; }
    }

    public long LongVar
    {

        get { return z; }

        set { z = value; }
    }

    public int x;
    public string y;
    public long z;
}

[GCOptimize]
[LuaCallCSharp]
public class TableAutoTransComplexClass
{
    public TableAutoTransComplexClass()
    {
    }

    public TableAutoTransComplexClass(int A, TableAutoTransSimpleClass B )
    {
        IntVar = A;
        ClassVar = B;
    }
    public int IntVar
    {

        get { return A; }

        set { A = value; }
    }

    public TableAutoTransSimpleClass ClassVar
    {

        get { return B; }

        set { B = value; }
    }

    public int A;
    public TableAutoTransSimpleClass B;
}

[GCOptimize]
[LuaCallCSharp]
public struct TableAutoTransSimpleStruct
{
    public byte a;
}

[GCOptimize]
[LuaCallCSharp]
public struct TableAutoTransComplexStruct
{
    public int a;
    public int b;
    public decimal c;
    public TableAutoTransSimpleStruct d;
}

[GCOptimize]
[LuaCallCSharp]
public class TestTableAutoTransClass
{
    public TableAutoTransSimpleClass SimpleClassMethod(TableAutoTransSimpleClass p)
    {
        return p;
    }

    public TableAutoTransComplexClass ComplexClassMethod(TableAutoTransComplexClass p)
    {
        return p;
    }

    public TableAutoTransSimpleStruct SimpleStructMethod(TableAutoTransSimpleStruct p)
    {
        return p;
    }

    public TableAutoTransComplexStruct ComplexStructMethod(TableAutoTransComplexStruct p)
    {
        return p;
    }

    public int OneListMethod(int[] args)
    {
        int value = 0;
        for (int i = 0; i < args.Length; i++)  
        {
            value += args[i];
         }
        return value;
    }

    public int TwoDimensionListMethod(int[][] args)
    {
        int value = 0;
        for (int i = 0; i < args.Length; i++)
        {
            foreach (int j in args[i])  
            {  
                value += j;  
            }  
        }
        return value;
    }
}

//Verify constructor support for ref and out parameters; added 2017-05-09 for v2.1.7.
[LuaCallCSharp]
public class ReferTestClass
{
    public ReferTestClass(int x, ref int y, out string z)
    {
        var_x = x;
        var_y = y;
        y = y - 1;
        z = "test1";
        var_z = z;
    }

    public ReferTestClass(int x, out string z)
    {
        var_x = x;
        var_y = 10;
        z = "test3";
        var_z = z;
    }

    public int Get_X_Y_ADD()
    {
        return var_x + var_y;
    }

    private int var_x;
    private int var_y;
    private string var_z;
}
// Packed into a plain Lua table by generated code (xlua_pushcstable); the
// reflection path passes it as a boxed object.
[GCOptimize(OptimizeFlag.PackAsTable)]
[LuaCallCSharp]
public struct PackAsTableStruct
{
	public int count;
	public double ratio;
	public PackAsTableStruct(int count, double ratio)
	{
		this.count = count;
		this.ratio = ratio;
	}
}

[LuaCallCSharp]
public static class NativeCoverageHelpers
{
	public static PackAsTableStruct MakePackAsTable(int count, double ratio)
	{
		return new PackAsTableStruct(count, ratio);
	}

	public static StaticPusherStructA MakeStructA(byte a, sbyte b)
	{
		return new StaticPusherStructA(a, b);
	}

	public static int Twice(int x)
	{
		return 2 * x;
	}

	// LuaTable.GetInPath/SetInPath (xlua_pgettable_bypath, xlua_psettable_bypath).
	public static object GetInPath(LuaTable table, string path)
	{
		return table.GetInPath<object>(path);
	}

	public static void SetInPath(LuaTable table, string path, object value)
	{
		table.SetInPath(path, value);
	}

	public static void SetEnv(LuaFunction function, LuaTable env)
	{
		function.SetEnv(env);
	}

	public static uint UintArg(uint value)
	{
		return value;
	}
}

// Integer parameters, fields and overloads for the float-to-integer rule
// (CaseInteger* in luaCallCs.lua). Wrappers for this type are emitted in the
// plain run and generated in the --gen-code runs; IntegerSlotsReflect below
// has the same members and always goes through reflection.
[LuaCallCSharp]
public class IntegerSlots
{
	public static sbyte SByte(sbyte v) { return v; }
	public static byte Byte(byte v) { return v; }
	public static short Short(short v) { return v; }
	public static ushort UShort(ushort v) { return v; }
	public static int Int(int v) { return v; }
	public static uint UInt(uint v) { return v; }
	public static long Long(long v) { return v; }
	public static ulong ULong(ulong v) { return v; }
	public static int Char(char v) { return v; }
	public static int Add(int a, int b) { return a + b; }

	public static sbyte SByteField;
	public static byte ByteField;
	public static short ShortField;
	public static ushort UShortField;
	public static int IntField;
	public static uint UIntField;
	public static long LongField;
	public static ulong ULongField;
	public static char CharField;
	public static int IntProperty { get; set; }

	public int Value;
	public int Echo(int v) { return v; }

	// The slots besides parameters and fields (CaseIntegerArrayElementRule,
	// CaseIntegerTableContentsRule, CaseIntegerParamsRule, CaseIntegerNullableRule,
	// CaseIntegerEnumRule): array elements, tables converted to arrays,
	// collections and structs, params arrays, nullable integers and enums.
	public static int[] Ints = new int[3];
	public static uint[] UInts = new uint[3];
	public static long[] Longs = new long[3];
	public static ulong[] ULongs = new ulong[3];
	public static short[] Shorts = new short[3];
	public static ushort[] UShorts = new ushort[3];
	public static sbyte[] SBytes = new sbyte[3];
	public static char[] Chars = new char[3];
	public static string JoinInts(int[] a) { return IntegerSlotText.Join(a); }
	public static string JoinUInts(uint[] a) { return IntegerSlotText.Join(a); }
	public static string JoinLongs(long[] a) { return IntegerSlotText.Join(a); }
	public static string JoinULongs(ulong[] a) { return IntegerSlotText.Join(a); }
	public static string JoinIntList(System.Collections.Generic.List<int> a) { return IntegerSlotText.Join(a); }
	public static string JoinByteList(System.Collections.Generic.List<byte> a) { return IntegerSlotText.Join(a); }
	public static string JoinDict(System.Collections.Generic.Dictionary<string, int> d) { return IntegerSlotText.Join(d); }
	public static int CountIntKeys(System.Collections.Generic.Dictionary<int, string> d) { return d.Count; }
	public static string StructArg(IntegerSlotStruct s) { return s.ToString(); }
	public static string PackedArg(StaticPusherStructB s) { return IntegerSlotText.Of(s); }
	public static string Params(params int[] a) { return IntegerSlotText.Join(a); }
	public static string ParamsLong(params long[] a) { return IntegerSlotText.Join(a); }
	public static string Nullable(int? v) { return v.HasValue ? v.Value.ToString() : "null"; }
	public static int? NullableField;
	public static string EnumArg(IntegerSlotEnum e) { return e.ToString(); }
	public static long LongEnumValue(IntegerSlotLongEnum e) { return (long)e; }

	public static string Pick(int v) { return "int"; }
	public static string Pick(double v) { return "double"; }
	public static string PickDoubleFirst(double v) { return "double"; }
	public static string PickDoubleFirst(int v) { return "int"; }
	public static string PickLong(long v) { return "long"; }
	public static string PickLong(double v) { return "double"; }
	public static string PickByte(byte v) { return "byte"; }
	public static string PickByte(float v) { return "float"; }
}

// Not public, so the bridge wraps it with reflection in every run.
internal class IntegerSlotsReflect
{
	public static sbyte SByte(sbyte v) { return v; }
	public static byte Byte(byte v) { return v; }
	public static short Short(short v) { return v; }
	public static ushort UShort(ushort v) { return v; }
	public static int Int(int v) { return v; }
	public static uint UInt(uint v) { return v; }
	public static long Long(long v) { return v; }
	public static ulong ULong(ulong v) { return v; }
	public static int Char(char v) { return v; }
	public static int Add(int a, int b) { return a + b; }

	public static sbyte SByteField;
	public static byte ByteField;
	public static short ShortField;
	public static ushort UShortField;
	public static int IntField;
	public static uint UIntField;
	public static long LongField;
	public static ulong ULongField;
	public static char CharField;
	public static int IntProperty { get; set; }

	public int Value;
	public int Echo(int v) { return v; }

	// The slots besides parameters and fields (CaseIntegerArrayElementRule,
	// CaseIntegerTableContentsRule, CaseIntegerParamsRule, CaseIntegerNullableRule,
	// CaseIntegerEnumRule): array elements, tables converted to arrays,
	// collections and structs, params arrays, nullable integers and enums.
	public static int[] Ints = new int[3];
	public static uint[] UInts = new uint[3];
	public static long[] Longs = new long[3];
	public static ulong[] ULongs = new ulong[3];
	public static short[] Shorts = new short[3];
	public static ushort[] UShorts = new ushort[3];
	public static sbyte[] SBytes = new sbyte[3];
	public static char[] Chars = new char[3];
	public static string JoinInts(int[] a) { return IntegerSlotText.Join(a); }
	public static string JoinUInts(uint[] a) { return IntegerSlotText.Join(a); }
	public static string JoinLongs(long[] a) { return IntegerSlotText.Join(a); }
	public static string JoinULongs(ulong[] a) { return IntegerSlotText.Join(a); }
	public static string JoinIntList(System.Collections.Generic.List<int> a) { return IntegerSlotText.Join(a); }
	public static string JoinByteList(System.Collections.Generic.List<byte> a) { return IntegerSlotText.Join(a); }
	public static string JoinDict(System.Collections.Generic.Dictionary<string, int> d) { return IntegerSlotText.Join(d); }
	public static int CountIntKeys(System.Collections.Generic.Dictionary<int, string> d) { return d.Count; }
	public static string StructArg(IntegerSlotStruct s) { return s.ToString(); }
	public static string PackedArg(StaticPusherStructB s) { return IntegerSlotText.Of(s); }
	public static string Params(params int[] a) { return IntegerSlotText.Join(a); }
	public static string ParamsLong(params long[] a) { return IntegerSlotText.Join(a); }
	public static string Nullable(int? v) { return v.HasValue ? v.Value.ToString() : "null"; }
	public static int? NullableField;
	public static string EnumArg(IntegerSlotEnum e) { return e.ToString(); }
	public static long LongEnumValue(IntegerSlotLongEnum e) { return (long)e; }

	public static string Pick(int v) { return "int"; }
	public static string Pick(double v) { return "double"; }
	public static string PickDoubleFirst(double v) { return "double"; }
	public static string PickDoubleFirst(int v) { return "int"; }
	public static string PickLong(long v) { return "long"; }
	public static string PickLong(double v) { return "double"; }
	public static string PickByte(byte v) { return "byte"; }
	public static string PickByte(float v) { return "float"; }
}

// Enums for CaseIntegerEnumRule: IntegerSlotEnum has generated wrappers in the
// --gen-code runs; IntegerSlotLongEnum is a 64-bit enum that only reflection
// converts (Enum.ToObject used to get a 32-bit int).
[LuaCallCSharp]
public enum IntegerSlotEnum
{
	A = 1,
	B = 2,
}

public enum IntegerSlotLongEnum : long
{
	Big = 1L << 40,
}

// A struct without [GCOptimize]: a Lua table converts to it field by field
// through reflection.
public struct IntegerSlotStruct
{
	public int I;
	public byte B;
	public long L;

	public override string ToString()
	{
		return I + "," + B + "," + L;
	}
}

public static class IntegerSlotText
{
	public static string Join<T>(System.Collections.Generic.IEnumerable<T> values)
	{
		var parts = new System.Collections.Generic.List<string>();
		foreach (T v in values) parts.Add(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture));
		return string.Join(",", parts.ToArray());
	}

	public static string Join<TKey, TValue>(System.Collections.Generic.Dictionary<TKey, TValue> d)
	{
		var parts = new System.Collections.Generic.List<string>();
		foreach (var kv in d) parts.Add(kv.Key + "=" + kv.Value);
		parts.Sort(StringComparer.Ordinal);
		return string.Join(",", parts.ToArray());
	}

	public static string Of(StaticPusherStructB s)
	{
		return s.shortVar + "," + s.ushortVar + "," + s.intVar + "," + s.uintVar;
	}

	public static string Of(int? v)
	{
		return v.HasValue ? v.Value.ToString() : "null";
	}
}

// Delegates and an interface whose results C# reads from Lua, for
// IntegerSlotResults: emitted (CodeEmit) in the plain run, generated in the
// --gen-code runs and source-generated with --source-gen.
[CSharpCallLua] public delegate int IntegerSlotIntResult();
[CSharpCallLua] public delegate byte IntegerSlotByteResult();
[CSharpCallLua] public delegate uint IntegerSlotUIntResult();
[CSharpCallLua] public delegate long IntegerSlotLongResult();
[CSharpCallLua] public delegate ulong IntegerSlotULongResult();
[CSharpCallLua] public delegate void IntegerSlotOutResult(out int a, out short b);
[CSharpCallLua] public delegate int[] IntegerSlotArrayResult();
[CSharpCallLua] public delegate IntegerSlotEnum IntegerSlotEnumResult();
[CSharpCallLua] public delegate int? IntegerSlotNullableResult();

[CSharpCallLua]
public interface IIntegerSlotSource
{
	int Next();
	int Count { get; }
	void Pair(out int a, out long b);
	byte Small();
}

// Values C# reads from Lua follow the integer rule too (CaseIntegerResultRule):
// delegate and interface results and out values, LuaFunction results,
// LuaTable.Get, GetInPath and ForEach, and tables converted to arrays,
// collections and structs on the way. A failed conversion throws an
// InvalidCastException ("number has no integer representation", or "value out
// of range" with XLUA_STRICT_INT_RANGE) and leaves the Lua stack top as it was.
// Each case runs in a LuaEnv of its own; Run returns the failures, "" if none.
public static class IntegerSlotResults
{
	const string NoInteger = "error: number has no integer representation";
#if XLUA_STRICT_INT_RANGE
	const bool Strict = true;
#else
	const bool Strict = false;
#endif
	const string OutOfRange = "error: value out of range";

	public static string Run()
	{
		var failures = new System.Collections.Generic.List<string>();
		var env = new LuaEnv();
		try
		{
			RunAll(env, failures);
		}
		finally
		{
			env.FullGc();
			GC.Collect();
			GC.WaitForPendingFinalizers();
			env.Tick();
			env.Dispose();
		}
		return string.Join("\n", failures.ToArray());
	}

	static int Top(LuaEnv env)
	{
		var property = typeof(LuaEnv).GetProperty("L", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
		return XLua.LuaDLL.Lua.lua_gettop((IntPtr)property.GetValue(env, null));
	}

	// The delegates and bridges made here are unreachable once it returns, so
	// that Run can dispose the LuaEnv.
	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
	static void RunAll(LuaEnv env, System.Collections.Generic.List<string> failures)
	{
		Action<string, string, Func<string>, string> check = (slot, lua, read, expected) =>
		{
			int before = Top(env);
			string got;
			try
			{
				got = read();
			}
			catch (Exception e)
			{
				got = "error: " + e.Message;
			}
			int after = Top(env);
			bool ok = expected.StartsWith("contains: ") ? got.StartsWith("error: ") && got.Contains(expected.Substring(10)) : got == expected;
			if (!ok) failures.Add(slot + " of " + lua + ": got " + got + ", expected " + expected);
			if (before != after) failures.Add(slot + " of " + lua + ": stack top " + before + " -> " + after);
		};
		Func<string, LuaFunction> fn = body => (LuaFunction)env.DoString("return function(...) " + body + " end")[0];
		Func<string, LuaTable> table = lua => (LuaTable)env.DoString("return " + lua)[0];
		string range = Strict ? OutOfRange : null;

		// Delegate results and out values.
		foreach (var c in new[] { new[] { "3.0", "3" }, new[] { "'12'", "12" }, new[] { "2.5", NoInteger }, new[] { "-0.5", NoInteger },
			new[] { "0/0", NoInteger }, new[] { "2^63", NoInteger }, new[] { "4294967301", range ?? "5" } })
		{
			check("int delegate result", c[0], () => fn("return " + c[0]).Cast<IntegerSlotIntResult>()().ToString(), c[1]);
		}
		check("byte delegate result", "2.5", () => fn("return 2.5").Cast<IntegerSlotByteResult>()().ToString(), NoInteger);
		check("byte delegate result", "300", () => fn("return 300").Cast<IntegerSlotByteResult>()().ToString(), range ?? "44");
		check("uint delegate result", "-1", () => fn("return -1").Cast<IntegerSlotUIntResult>()().ToString(), range ?? "4294967295");
		check("uint delegate result", "1.5", () => fn("return 1.5").Cast<IntegerSlotUIntResult>()().ToString(), NoInteger);
		check("long delegate result", "2^53", () => fn("return 2^53").Cast<IntegerSlotLongResult>()().ToString(), "9007199254740992");
		check("long delegate result", "2^63", () => fn("return 2^63").Cast<IntegerSlotLongResult>()().ToString(), NoInteger);
		check("long delegate result", "'12'", () => fn("return '12'").Cast<IntegerSlotLongResult>()().ToString(), "12");
		check("ulong delegate result", "-1", () => fn("return -1").Cast<IntegerSlotULongResult>()().ToString(), "18446744073709551615");
		check("ulong delegate result", "0.5", () => fn("return 0.5").Cast<IntegerSlotULongResult>()().ToString(), NoInteger);
		Func<string, string> outs = lua =>
		{
			int a;
			short b;
			fn("return " + lua).Cast<IntegerSlotOutResult>()(out a, out b);
			return a + "," + b;
		};
		check("delegate out values", "3.0, 4.0", () => outs("3.0, 4.0"), "3,4");
		check("delegate out values", "1, 2.5", () => outs("1, 2.5"), NoInteger);
		check("delegate out values", "2.5, 1", () => outs("2.5, 1"), NoInteger);
		check("delegate out values", "1, 32768", () => outs("1, 32768"), range ?? "1,-32768");
		check("int[] delegate result", "{1, 2.0}", () => IntegerSlotText.Join(fn("return {1, 2.0}").Cast<IntegerSlotArrayResult>()()), "1,2");
		check("int[] delegate result", "{1, 2.5}", () => IntegerSlotText.Join(fn("return {1, 2.5}").Cast<IntegerSlotArrayResult>()()), NoInteger);
		check("enum delegate result", "2.0", () => fn("return 2.0").Cast<IntegerSlotEnumResult>()().ToString(), "B");
		check("enum delegate result", "2.5", () => fn("return 2.5").Cast<IntegerSlotEnumResult>()().ToString(), NoInteger);
		check("int? delegate result", "nil", () => IntegerSlotText.Of(fn("return nil").Cast<IntegerSlotNullableResult>()()), "null");
		check("int? delegate result", "3.0", () => IntegerSlotText.Of(fn("return 3.0").Cast<IntegerSlotNullableResult>()()), "3");
		check("int? delegate result", "2.5", () => IntegerSlotText.Of(fn("return 2.5").Cast<IntegerSlotNullableResult>()()), NoInteger);

		// Interface bridge results, a property and out values.
		Func<string, IIntegerSlotSource> source = v => table("{ Next = function(self) return " + v + " end, Count = " + v
			+ ", Pair = function(self) return " + v + ", " + v + " end, Small = function(self) return " + v + " end }").Cast<IIntegerSlotSource>();
		Func<IIntegerSlotSource, string> pair = s =>
		{
			int a;
			long b;
			s.Pair(out a, out b);
			return a + "," + b;
		};
		check("interface method result", "3.0", () => source("3.0").Next().ToString(), "3");
		check("interface method result", "2.5", () => source("2.5").Next().ToString(), NoInteger);
		check("interface property", "3.0", () => source("3.0").Count.ToString(), "3");
		check("interface property", "2.5", () => source("2.5").Count.ToString(), NoInteger);
		check("interface out values", "3.0", () => pair(source("3.0")), "3,3");
		check("interface out values", "2.5", () => pair(source("2.5")), NoInteger);
		check("interface byte result", "256", () => source("256").Small().ToString(), range ?? "0");

		// LuaFunction results.
		check("LuaFunction.Func<int, int>", "x + 0.5", () => fn("local x = ... return x + 0.5").Func<int, int>(1).ToString(), NoInteger);
		check("LuaFunction.Func<int, int>", "x * 2.0", () => fn("local x = ... return x * 2.0").Func<int, int>(3).ToString(), "6");
		check("LuaFunction.Call with return types", "1, 2.5", () => IntegerSlotText.Join(fn("return 1, 2.5").Call(new object[0], new[] { typeof(int), typeof(int) })), NoInteger);
		check("LuaFunction.Call with return types", "1, 2.0", () => IntegerSlotText.Join(fn("return 1, 2.0").Call(new object[0], new[] { typeof(int), typeof(int) })), "1,2");

		// LuaTable reads, and tables converted on the way.
		check("LuaTable.Get<string, int>", "2.5", () => table("{ v = 2.5 }").Get<string, int>("v").ToString(), NoInteger);
		check("LuaTable.Get<string, int>", "3.0", () => table("{ v = 3.0 }").Get<string, int>("v").ToString(), "3");
		check("LuaTable.Get<string, ushort>", "-1", () => table("{ v = -1 }").Get<string, ushort>("v").ToString(), range ?? "65535");
		check("LuaTable.Get<string, long>", "0.5", () => table("{ v = 0.5 }").Get<string, long>("v").ToString(), NoInteger);
		check("LuaTable.Get<string, ulong>", "1.5", () => table("{ v = 1.5 }").Get<string, ulong>("v").ToString(), NoInteger);
		check("LuaTable.Get<string, int?>", "2.5", () => IntegerSlotText.Of(table("{ v = 2.5 }").Get<string, int?>("v")), NoInteger);
		check("LuaTable.Get<string, IntegerSlotEnum>", "1.5", () => table("{ v = 1.5 }").Get<string, IntegerSlotEnum>("v").ToString(), NoInteger);
		check("LuaTable.Get<string, IntegerSlotLongEnum>", "2^40", () => ((long)table("{ v = 2^40 }").Get<string, IntegerSlotLongEnum>("v")).ToString(), "1099511627776");
		check("LuaTable.GetInPath<int>", "2.5", () => table("{ a = { v = 2.5 } }").GetInPath<int>("a.v").ToString(), NoInteger);
		check("LuaTable.Get<int, int>", "[1] = 2.5", () => table("{ 2.5 }").Get<int, int>(1).ToString(), NoInteger);
		check("LuaTable.Get<string, int[]>", "{1, 2.5}", () => IntegerSlotText.Join(table("{ v = {1, 2.5} }").Get<string, int[]>("v")), NoInteger);
		check("LuaTable.Get<string, int[]>", "{1, 2.0}", () => IntegerSlotText.Join(table("{ v = {1, 2.0} }").Get<string, int[]>("v")), "1,2");
		check("LuaTable.Get<string, List<int>>", "{1, 2.5}", () => IntegerSlotText.Join(table("{ v = {1, 2.5} }").Get<string, System.Collections.Generic.List<int>>("v")), NoInteger);
		check("LuaTable.Get<string, List<int>>", "{1, 'x', 3.0}", () => IntegerSlotText.Join(table("{ v = {1, 'x', 3.0} }").Get<string, System.Collections.Generic.List<int>>("v")), "1,3");
		check("LuaTable.Get<string, Dictionary<string, int>>", "{a = 2.5}", () => IntegerSlotText.Join(table("{ v = {a = 2.5} }").Get<string, System.Collections.Generic.Dictionary<string, int>>("v")), NoInteger);
		check("LuaTable.Get<string, Dictionary<int, string>>", "{[1.5] = 'x', [2] = 'y'}", () => IntegerSlotText.Join(table("{ v = {[1.5] = 'x', [2] = 'y'} }").Get<string, System.Collections.Generic.Dictionary<int, string>>("v")), "2=y");
		check("LuaTable.Get<string, IntegerSlotStruct>", "{I = 2.5}", () => table("{ v = {I = 2.5} }").Get<string, IntegerSlotStruct>("v").ToString(), "contains: exception in tran I, msg=number has no integer representation");
		check("LuaTable.Get<string, IntegerSlotStruct>", "{I = 3.0, B = 7, L = 2^40}", () => table("{ v = {I = 3.0, B = 7, L = 2^40} }").Get<string, IntegerSlotStruct>("v").ToString(), "3,7,1099511627776");
		check("LuaTable.Get<string, StaticPusherStructB>", "{intVar = 2.5}", () => IntegerSlotText.Of(table("{ v = {intVar = 2.5} }").Get<string, StaticPusherStructB>("v")), "contains: number has no integer representation");
		check("LuaTable.Get<string, StaticPusherStructB>", "{intVar = 3.0, shortVar = 2}", () => IntegerSlotText.Of(table("{ v = {intVar = 3.0, shortVar = 2} }").Get<string, StaticPusherStructB>("v")), "2,0,3,0");
		check("LuaTable.ForEach<string, int>", "{a = 2.5}", () =>
		{
			int n = 0;
			table("{ a = 2.5 }").ForEach<string, int>((k, v) => n += v);
			return n.ToString();
		}, NoInteger);
		check("LuaTable.Cast<int[]>", "{1, 0.5}", () => IntegerSlotText.Join(table("{1, 0.5}").Cast<int[]>()), NoInteger);
	}
}

// Raw lua_CSFunction callbacks for the LuaAPI.StackToString cases. Each returns the
// dump and whether the stack (its top and the type of every slot) was left unchanged.
[LuaCallCSharp]
public static class StackDumpHelpers
{
	[MonoPInvokeCallback(typeof(XLua.LuaDLL.lua_CSFunction))]
	static int Dump(IntPtr L)
	{
		return Report(L, -1);
	}

	[MonoPInvokeCallback(typeof(XLua.LuaDLL.lua_CSFunction))]
	static int DumpShort(IntPtr L)
	{
		return Report(L, 4);
	}

	static int Report(IntPtr L, int maxStringLength)
	{
		int top = XLua.LuaDLL.Bridge.lua_gettop(L);
		LuaTypes[] types = new LuaTypes[top + 1];
		for (int i = 1; i <= top; i++) types[i] = XLua.LuaDLL.Bridge.lua_type(L, i);
		string dump = maxStringLength < 0 ? XLua.LuaDLL.Bridge.StackToString(L)
			: XLua.LuaDLL.Bridge.StackToString(L, maxStringLength);
		bool unchanged = XLua.LuaDLL.Bridge.lua_gettop(L) == top;
		for (int i = 1; unchanged && i <= top; i++) unchanged = XLua.LuaDLL.Bridge.lua_type(L, i) == types[i];
		XLua.LuaDLL.Bridge.lua_pushstring(L, dump);
		XLua.LuaDLL.Bridge.lua_pushboolean(L, unchanged);
		return 2;
	}

	public static XLua.LuaDLL.lua_CSFunction GetDump()
	{
		return Dump;
	}

	public static XLua.LuaDLL.lua_CSFunction GetDumpShort()
	{
		return DumpShort;
	}
}

[LuaCallCSharp]
public class StackDumpThrowingToString
{
	public override string ToString()
	{
		throw new InvalidOperationException("boom");
	}
}

// Strings for the UTF-8 cases. Lua strings are bytes: xLua reads them as UTF-8,
// with U+FFFD for invalid bytes, and writes C# strings as UTF-8. The constants
// are spelled with escapes so this file needs no particular encoding.
[LuaCallCSharp]
public static class Utf8StringHelpers
{
	public const string Turkish = "şeğiıİçöü";
	public const string Chinese = "中文";
	// U+1F600, outside the BMP: a surrogate pair in C#, four bytes in Lua.
	public const string Emoji = "\U0001F600";
	public const string WithNul = "x\0y";
	// A lone surrogate has no UTF-8 form.
	public const string LoneSurrogate = "a\uD800b";

	public static string Echo(string s)
	{
		return s;
	}

	public static int Length(string s)
	{
		return s.Length;
	}

	// The UTF-16 code units in hex, such as "0061 FFFD".
	public static string CodeUnits(string s)
	{
		var sb = new System.Text.StringBuilder();
		foreach (char c in s)
		{
			if (sb.Length > 0) sb.Append(' ');
			sb.Append(((int)c).ToString("X4"));
		}
		return sb.ToString();
	}

	public static string BytesHex(byte[] bytes)
	{
		return BitConverter.ToString(bytes).Replace('-', ' ');
	}

	// C# passes s to a Lua function and returns the string it gives back.
	public static string ThroughLua(LuaFunction f, string s)
	{
		object[] r = f.Call(s);
		return (string)r[0];
	}

	// The message of the Lua error that f raises.
	public static string ErrorMessage(LuaFunction f)
	{
		try
		{
			f.Call();
		}
		catch (LuaException e)
		{
			return e.Message;
		}
		return null;
	}

	// A raw callback that joins its arguments with LuaAPI.lua_tostring, the way
	// the editor's print does, and returns the code units of the result.
	[MonoPInvokeCallback(typeof(XLua.LuaDLL.lua_CSFunction))]
	static int Join(IntPtr L)
	{
		string s = "";
		int top = XLua.LuaDLL.Bridge.lua_gettop(L);
		for (int i = 1; i <= top; i++) s += XLua.LuaDLL.Bridge.lua_tostring(L, i);
		XLua.LuaDLL.Bridge.lua_pushstring(L, CodeUnits(s));
		return 1;
	}

	public static XLua.LuaDLL.lua_CSFunction GetJoin()
	{
		return Join;
	}
}

// Members with Turkish and Chinese names, spelled with \u escapes so this file
// needs no particular encoding: Sehir and Carp/Agirlik with Turkish letters, and
// "shuliang", "mingzi" and "jiabei" in Chinese. Wrappers register member names
// with LuaAPI.xlua_pushasciistring, and generated enum wrappers compare them with
// LuaAPI.xlua_is_eq_str, so the names must reach Lua as their UTF-8 bytes. The
// long names (120 CJK characters, 360 bytes) do not fit the 256-byte buffer that
// xlua_pushasciistring reuses.
[LuaCallCSharp]
public class Utf8Names
{
	public int \u015Eehir = 1;
	public int \u6570\u91CF = 2;
	public int \u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F = 3;
	public static int \u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957FStatic = 5;
	public string A\u011F\u0131rl\u0131k { get; set; }
	public string \u540D\u5B57 { get; set; }

	public int \u00C7arp(int x)
	{
		return x * 3;
	}

	public int \u52A0\u500D(int x)
	{
		return x * 2;
	}

	public int \u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD()
	{
		return 4;
	}

	// The same members on a non-public type, which xLua always wraps by reflection.
	public static object MakeReflect()
	{
		return new Utf8NamesReflect();
	}

	public static int EnumValue(Utf8Enum e)
	{
		return (int)e;
	}
}

class Utf8NamesReflect
{
	public int \u015Eehir = 1;
	public int \u6570\u91CF = 2;
	public int \u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F = 3;
	public string A\u011F\u0131rl\u0131k { get; set; }
	public string \u540D\u5B57 { get; set; }

	public int \u00C7arp(int x)
	{
		return x * 3;
	}

	public int \u52A0\u500D(int x)
	{
		return x * 2;
	}

	public int \u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD\u5BBD()
	{
		return 4;
	}
}

[LuaCallCSharp]
public enum Utf8Enum
{
	\u015Eimdi = 1,
	\u4E2D\u6587 = 2,
	\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F\u957F = 3,
}

// xlua.metatable_operation set: CaseMetatableOperationSetKeepsBridge replaces
// this type's metatable with one that lacks the bridge tag and __gc. Each
// instance records a weak reference, so the test can check that the object
// pool released it once Lua collected the userdata.
[LuaCallCSharp]
public class MetatableOperationProbe
{
	static readonly System.Collections.Generic.List<WeakReference> created = new System.Collections.Generic.List<WeakReference>();

	public int Value;

	public MetatableOperationProbe(int value)
	{
		Value = value;
		created.Add(new WeakReference(this));
	}

	public int Twice()
	{
		return 2 * Value;
	}

	// The bridge reads the argument as this type only if it recognises the userdata.
	public static bool IsProbe(object o)
	{
		return o is MetatableOperationProbe;
	}

	public static void Reset()
	{
		created.Clear();
	}

	public static int AliveCount()
	{
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();
		int alive = 0;
		foreach (var w in created)
		{
			if (w.IsAlive) alive++;
		}
		return alive;
	}
}
