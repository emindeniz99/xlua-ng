// size_report.py copies this into Assets/Editor for the +sg and +link* variants: a measurement of what
// link.xml preservation costs (build/SIZE.md, "What link.xml preservation costs"). Not part of xLua.
//
// With one of the defines below (size_report.py writes it into csc.rsp), an IUnityLinkerProcessor gives
// UnityLinker one more link.xml for every type that Assembly-CSharp configures at assembly level with
// [LuaCallCSharp] or [ReflectionUse], named as the source generator names them (the generic type
// definition, nested types with '/'):
//
//   XLUA_SIZE_LINKXML_ALL     <type fullname="T" preserve="all"/>, which the source generator writes for
//                             [ReflectionUse] and the removed Generate Code (LinkXmlGen) wrote for every
//                             [LuaCallCSharp] and [ReflectionUse] type
//   XLUA_SIZE_LINKXML_PUBLIC  the public members T declares, by name (<method>, <field>, <property>,
//                             <event>; constructors as ".ctor"): what the reflection fallback reaches
//                             without xlua.private_accessible
//   XLUA_SIZE_LINKXML_TYPE    <type fullname="T" preserve="nothing"/>: the type alone (CS.T, typeof)
//
// The member names come from the Editor's assemblies, as the source generator's link.xml does
// (SourceGenChecks.MergedLinkXml); a name the player's assembly lacks is a linker warning.
//
// In every variant it also copies the link.xml files UnityLinker gets (this one and the source generator's
// Temp/XLuaSourceGen/link.xml) next to the player, so the report shows what was preserved.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.UnityLinker;

class SizeLinkXml : IUnityLinkerProcessor
{
    // After the source generator's SourceGenLinkXml (order 0), so its file exists when this one copies it.
    public int callbackOrder { get { return 100; } }

#if XLUA_SIZE_LINKXML_ALL
    static readonly string Mode = "all";
#elif XLUA_SIZE_LINKXML_PUBLIC
    static readonly string Mode = "public";
#elif XLUA_SIZE_LINKXML_TYPE
    static readonly string Mode = "type";
#else
    static readonly string Mode = null;
#endif

    public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
    {
        string root = Environment.GetEnvironmentVariable("XLUA_SMOKE_OUTPUT_ROOT");
        string generated = Path.GetFullPath(Path.Combine("Temp", "XLuaSourceGen", "link.xml"));
        if (!string.IsNullOrEmpty(root) && File.Exists(generated))
        {
            File.Copy(generated, Path.Combine(root, "link-sourcegen.xml"), true);
        }
        if (Mode == null) return null;
        string xml = Document(ConfiguredTypes());
        string path = Path.GetFullPath(Path.Combine("Temp", "XLuaSizeLinkXml", "link.xml"));
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, xml, new UTF8Encoding(false));
        if (!string.IsNullOrEmpty(root)) File.WriteAllText(Path.Combine(root, "link-" + Mode + ".xml"), xml, new UTF8Encoding(false));
        UnityEngine.Debug.Log("XLUA_SIZE_LINKXML: " + Mode + ", " + path);
        return path;
    }

    static List<Type> ConfiguredTypes()
    {
        var types = new List<Type>();
        var assembly = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Assembly-CSharp");
        foreach (var attribute in assembly.GetCustomAttributes(false))
        {
            string name = attribute.GetType().FullName;
            if (name != "XLua.LuaCallCSharpAttribute" && name != "XLua.ReflectionUseAttribute") continue;
            var listed = attribute.GetType().GetProperty("Types").GetValue(attribute, null) as Type[];
            if (listed == null) continue;
            foreach (var t in listed)
            {
                var d = t.IsGenericType ? t.GetGenericTypeDefinition() : t;
                if (!types.Contains(d)) types.Add(d);
            }
        }
        return types;
    }

    static string Xml(string s)
    {
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }

    static string Document(List<Type> types)
    {
        const BindingFlags Declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var sb = new StringBuilder("<linker>\n");
        foreach (var byAssembly in types.GroupBy(t => t.Assembly.GetName().Name).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            sb.Append("  <assembly fullname=\"").Append(Xml(byAssembly.Key)).Append("\">\n");
            foreach (var t in byAssembly.OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                string fullname = Xml(t.FullName.Replace('+', '/'));
                if (Mode == "all")
                {
                    sb.Append("    <type fullname=\"").Append(fullname).Append("\" preserve=\"all\"/>\n");
                    continue;
                }
                if (Mode == "type")
                {
                    sb.Append("    <type fullname=\"").Append(fullname).Append("\" preserve=\"nothing\"/>\n");
                    continue;
                }
                var members = new SortedSet<string>(StringComparer.Ordinal);
                if (t.GetConstructors(Declared).Length > 0) members.Add("method|.ctor");
                foreach (var m in t.GetMethods(Declared)) members.Add("method|" + m.Name);
                foreach (var f in t.GetFields(Declared)) members.Add("field|" + f.Name);
                foreach (var p in t.GetProperties(Declared)) members.Add("property|" + p.Name);
                foreach (var e in t.GetEvents(Declared)) members.Add("event|" + e.Name);
                // As the source generator writes its member entries: no preserve attribute, so only the
                // listed members are kept (checked in the stripped DLLs, Test/MobileSmoke/README.md, M5).
                sb.Append("    <type fullname=\"").Append(fullname).Append("\">\n");
                foreach (var m in members)
                {
                    var kv = m.Split('|');
                    sb.Append("      <").Append(kv[0]).Append(" name=\"").Append(Xml(kv[1])).Append("\"/>\n");
                }
                sb.Append("    </type>\n");
            }
            sb.Append("  </assembly>\n");
        }
        return sb.Append("</linker>\n").ToString();
    }
}
