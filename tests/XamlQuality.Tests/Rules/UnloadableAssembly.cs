using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;

namespace XamlQuality.Tests.Rules;

/// <summary>
/// A consumer's build output whose dependency is not beside it: <c>Consumer.dll</c>, loaded where
/// <c>Dep.dll</c>, which it references, cannot be found.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>What a scan of a library's <c>bin</c> folder is handed.</b> A library's build output carries
/// its own assembly and not the framework it is built on, so a control there fails to load, and so
/// does anything that names a framework type. Measured on two libraries' Release output: two rules
/// threw on it, and a third reported the consumer's own controls as types it had not been given.
/// </para>
/// <para>
/// ⚠ <b>Emitted at test time, so no binary is checked in and nothing here can load <c>Dep</c>.</b>
/// <c>Dep.dll</c> is written to one folder and <c>Consumer.dll</c> to another, and the consumer is
/// loaded from its file, because the rules read the names of types that did not load from that
/// file's metadata. The files stay locked until the process exits, so each run writes its own folder
/// and clears the ones earlier runs left.
/// </para>
/// <list type="bullet">
/// <item><c>Thing</c> derives from <c>Dep</c>'s <c>DepBase</c>, so it does not load. It declares
/// <c>PART_Knob</c>, and holds a nested <c>Closure</c> that loads while its enclosing type does not,
/// as a lambda's closure does.</item>
/// <item><c>Plain</c> loads. It declares <c>PART_Plain</c>, and its <c>Build</c> method has a local of
/// type <c>DepBase</c>, so reading that method's body fails.</item>
/// </list>
/// </remarks>
internal static class UnloadableAssembly
{
    private static readonly Lazy<Assembly> Built = new(Build);

    /// <summary>The loaded <c>Consumer</c> assembly, built once per test run.</summary>
    public static Assembly Consumer => Built.Value;

    private static Assembly Build()
    {
        string temp = Path.GetTempPath();
        foreach (string stale in Directory.EnumerateDirectories(temp, "xq-unloadable-*"))
        {
            try
            {
                Directory.Delete(stale, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Still loaded by a run in progress.
            }
        }

        string root = Directory.CreateTempSubdirectory("xq-unloadable-").FullName;
        string depFile = Path.Combine(Directory.CreateDirectory(Path.Combine(root, "dep")).FullName, "Dep.dll");
        string consumerFile = Path.Combine(Directory.CreateDirectory(Path.Combine(root, "consumer")).FullName, "Consumer.dll");

        PersistedAssemblyBuilder dep = new(new AssemblyName("Dep"), typeof(object).Assembly);
        TypeBuilder depBase = dep.DefineDynamicModule("Dep").DefineType("Fixtures.DepBase", TypeAttributes.Public | TypeAttributes.Class);
        depBase.DefineDefaultConstructor(MethodAttributes.Public);
        depBase.CreateType();
        dep.Save(depFile);

        // The consumer is emitted against a loaded Dep, which is then unloaded, so the assembly
        // reference is real and nothing in this process can resolve it afterwards.
        AssemblyLoadContext building = new("xq-unloadable-dep", isCollectible: true);
        Type parent = building.LoadFromAssemblyPath(depFile).GetType("Fixtures.DepBase", throwOnError: true)!;

        PersistedAssemblyBuilder consumer = new(new AssemblyName("Consumer"), typeof(object).Assembly);
        ModuleBuilder module = consumer.DefineDynamicModule("Consumer");

        TypeBuilder thing = module.DefineType("Fixtures.Thing", TypeAttributes.Public | TypeAttributes.Class, parent);
        thing.DefineField("PART_Knob", typeof(string), FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal)
            .SetConstant("PART_Knob");
        thing.DefineDefaultConstructor(MethodAttributes.Public);
        TypeBuilder closure = thing.DefineNestedType("Closure", TypeAttributes.NestedPublic | TypeAttributes.Class);
        closure.DefineDefaultConstructor(MethodAttributes.Public);
        thing.CreateType();
        closure.CreateType();

        TypeBuilder plain = module.DefineType("Fixtures.Plain", TypeAttributes.Public | TypeAttributes.Class);
        plain.DefineField("PART_Plain", typeof(string), FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal)
            .SetConstant("PART_Plain");
        ILGenerator body = plain.DefineMethod("Build", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes)
            .GetILGenerator();
        body.DeclareLocal(parent);
        body.Emit(OpCodes.Ret);
        plain.CreateType();

        consumer.Save(consumerFile);
        building.Unload();

        return new AssemblyLoadContext("xq-unloadable-consumer").LoadFromAssemblyPath(consumerFile);
    }
}
