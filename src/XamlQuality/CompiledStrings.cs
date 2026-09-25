using System.Reflection;
using System.Reflection.Emit;

namespace Bennewitz.Ninja.XamlQuality;

/// <summary>What a method's compiled code does: the string literals it loads, and what it calls.</summary>
/// <remarks>
/// <para>
/// ⭐ <b>What reflection alone cannot see.</b> A constant is metadata; a literal passed straight to a
/// call is only an <c>ldstr</c> instruction in a method body, and the type a method constructs is only
/// a <c>newobj</c>. Reading the IL is the one way to find either without the source, and it needs
/// nothing beyond <see cref="System.Reflection"/>.
/// </para>
/// <para>
/// ⚠ <b>The same literal means different things by what it is passed to.</b> Compiled XAML loads
/// every element name too, to register it or to build a selector, and those are not lookups. So each
/// literal carries the name of the next method the code calls, which is how a caller tells a lookup
/// from a registration.
/// </para>
/// <para>
/// ⚠ <b>Every operand is stepped over, not searched.</b> Scanning the bytes for the <c>ldstr</c>
/// opcode would misread any operand byte that happens to equal it, so each instruction is decoded
/// and its operand skipped by its declared size.
/// </para>
/// </remarks>
internal static class CompiledStrings
{
    private static readonly OpCode[] OneByte = new OpCode[0x100];
    private static readonly OpCode[] TwoByte = new OpCode[0x100];

    static CompiledStrings()
    {
        foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not OpCode code)
            {
                continue;
            }

            ushort value = unchecked((ushort)code.Value);
            if (value < 0x100)
            {
                OneByte[value] = code;
            }
            else if ((value & 0xFF00) == 0xFE00)
            {
                TwoByte[value & 0xFF] = code;
            }
        }
    }

    /// <summary>
    /// Every string <paramref name="method"/> loads with <c>ldstr</c> that <paramref name="wanted"/>
    /// accepts, each with the name of the next method called after it. Empty when the method has no
    /// IL body.
    /// </summary>
    /// <remarks>
    /// The next call is <c>null</c> when none follows, or when its target cannot be resolved, for
    /// instance because an assembly it lives in does not load.
    /// </remarks>
    internal static IReadOnlyList<(string Value, string? NextCall)> LoadedBy(MethodBase method, Func<string, bool> wanted)
    {
        if (BodyOf(method) is not { } il)
        {
            return [];
        }

        List<(string Value, string? NextCall)> loaded = [];
        List<string> pending = [];
        foreach ((OpCode code, int operand) in Instructions(il))
        {
            if (code.OperandType == OperandType.InlineString && operand + 4 <= il.Length)
            {
                string? value = Resolve(() => method.Module.ResolveString(BitConverter.ToInt32(il, operand)));
                if (value is not null && wanted(value))
                {
                    pending.Add(value);
                }
            }
            else if (pending.Count > 0
                     && (code == OpCodes.Call || code == OpCodes.Callvirt || code == OpCodes.Newobj)
                     && operand + 4 <= il.Length)
            {
                string? callee = Resolve(() => method.Module.ResolveMethod(BitConverter.ToInt32(il, operand))?.Name);
                loaded.AddRange(pending.Select(value => (value, callee)));
                pending.Clear();
            }
        }

        loaded.AddRange(pending.Select(value => (value, (string?)null)));
        return loaded;
    }

    /// <summary>
    /// Every type <paramref name="method"/> constructs with <c>newobj</c>, in the order its code does.
    /// Empty when the method has no IL body.
    /// </summary>
    /// <remarks>
    /// A constructor whose type cannot be resolved, for instance because its assembly does not load,
    /// is left out rather than guessed at.
    /// </remarks>
    internal static IReadOnlyList<Type> ConstructedBy(MethodBase method)
    {
        if (BodyOf(method) is not { } il)
        {
            return [];
        }

        Type[]? typeArguments = method.DeclaringType is { IsGenericType: true } owner ? owner.GetGenericArguments() : null;
        Type[]? methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;

        List<Type> constructed = [];
        foreach ((OpCode code, int operand) in Instructions(il))
        {
            if (code == OpCodes.Newobj
                && operand + 4 <= il.Length
                && Resolve(() => method.Module.ResolveMethod(BitConverter.ToInt32(il, operand), typeArguments, methodArguments)?.DeclaringType)
                    is { } type)
            {
                constructed.Add(type);
            }
        }

        return constructed;
    }

    /// <summary>
    /// Every type <paramref name="method"/> loads with <c>ldtoken</c>, the instruction a <c>typeof</c>
    /// compiles to, in the order its code does. Empty when the method has no IL body.
    /// </summary>
    internal static IReadOnlyList<Type> TypeOfsIn(MethodBase method)
    {
        if (BodyOf(method) is not { } il)
        {
            return [];
        }

        Type[]? typeArguments = method.DeclaringType is { IsGenericType: true } owner ? owner.GetGenericArguments() : null;
        Type[]? methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;

        List<Type> loaded = [];
        foreach ((OpCode code, int operand) in Instructions(il))
        {
            if (code == OpCodes.Ldtoken
                && operand + 4 <= il.Length
                && Resolve(() => method.Module.ResolveMember(BitConverter.ToInt32(il, operand), typeArguments, methodArguments))
                    is Type type)
            {
                loaded.Add(type);
            }
        }

        return loaded;
    }

    /// <summary>
    /// Every method or constructor <paramref name="method"/> calls, with <c>call</c>, <c>callvirt</c>
    /// or <c>newobj</c>, in the order its code does. Empty when the method has no IL body.
    /// </summary>
    /// <remarks>
    /// A call whose target cannot be resolved, for instance because its assembly does not load, is
    /// left out rather than guessed at.
    /// </remarks>
    internal static IReadOnlyList<MethodBase> CalledBy(MethodBase method)
    {
        if (BodyOf(method) is not { } il)
        {
            return [];
        }

        Type[]? typeArguments = method.DeclaringType is { IsGenericType: true } owner ? owner.GetGenericArguments() : null;
        Type[]? methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;

        List<MethodBase> called = [];
        foreach ((OpCode code, int operand) in Instructions(il))
        {
            if ((code == OpCodes.Call || code == OpCodes.Callvirt || code == OpCodes.Newobj)
                && operand + 4 <= il.Length
                && Resolve(() => method.Module.ResolveMethod(BitConverter.ToInt32(il, operand), typeArguments, methodArguments))
                    is { } callee)
            {
                called.Add(callee);
            }
        }

        return called;
    }

    /// <summary>A method's IL, or <c>null</c> when it has none or it cannot be read.</summary>
    private static byte[]? BodyOf(MethodBase method)
    {
        try
        {
            return method.GetMethodBody()?.GetILAsByteArray();
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or BadImageFormatException)
        {
            return null;
        }
    }

    /// <summary>Each instruction in order, with the offset of its operand.</summary>
    private static IEnumerable<(OpCode Code, int Operand)> Instructions(byte[] il)
    {
        int at = 0;
        while (at < il.Length)
        {
            OpCode code;
            if (il[at] == 0xFE && at + 1 < il.Length)
            {
                code = TwoByte[il[at + 1]];
                at += 2;
            }
            else
            {
                code = OneByte[il[at]];
                at += 1;
            }

            yield return (code, at);
            at += OperandSize(code.OperandType, il, at);
        }
    }

    /// <summary>A metadata token resolved, or <c>null</c> when this module cannot resolve it.</summary>
    private static T? Resolve<T>(Func<T?> resolve)
        where T : class
    {
        try
        {
            return resolve();
        }
        catch (Exception ex) when (ex is ArgumentException or TypeLoadException or FileNotFoundException
                                       or FileLoadException or BadImageFormatException or MissingMemberException)
        {
            return null;
        }
    }

    private static int OperandSize(OperandType type, byte[] il, int at) => type switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => at + 4 <= il.Length ? 4 + (4 * BitConverter.ToInt32(il, at)) : 4,
        _ => 4,
    };
}
