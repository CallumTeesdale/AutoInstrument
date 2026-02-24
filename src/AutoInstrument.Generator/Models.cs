using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoInstrument.Generator;

internal sealed record InstrumentedMethodInfo
{
    public string Namespace { get; init; } = "";
    public string ClassName { get; init; } = "";
    public string MethodName { get; init; } = "";
    public string FullyQualifiedClassName { get; init; } = "";
    public string? SpanName { get; init; }
    public string AssemblyName { get; init; } = "";
    public string? ActivitySourceName { get; init; }
    public string? DefaultActivitySourceName { get; init; }
    public string EffectiveActivitySourceName => ActivitySourceName ?? DefaultActivitySourceName ?? AssemblyName;
    public string EffectiveSpanName => SpanName ?? $"{ClassName}.{MethodName}";

    public string ReturnType { get; init; } = "void";
    public bool IsAsync { get; init; }
    public bool ReturnsVoid { get; init; }
    public bool IsAwaitable { get; init; }
    public bool HasGenericReturn { get; init; }
    public string? InnerReturnType { get; init; }

    public bool IsStatic { get; init; }
    public bool IsExtensionMethod { get; init; }

    public EquatableArray<ParameterInfo> Parameters { get; init; } = new(Array.Empty<ParameterInfo>());

    public EquatableArray<string> Skip { get; init; } = new(Array.Empty<string>());
    public EquatableArray<string> Fields { get; init; } = new(Array.Empty<string>());
    public bool RecordReturnValue { get; init; }
    public bool RecordException { get; init; } = true;
    public int Kind { get; init; }

    // New features
    public bool RecordSuccess { get; init; }
    public bool IgnoreCancellation { get; init; } = true;
    public string? Condition { get; init; }
    public string? LinkTo { get; init; }
    public EquatableArray<TagMemberInfo> TagMembers { get; init; } = new(Array.Empty<TagMemberInfo>());
    public int TagNamingConvention { get; init; } // 0=Method, 1=Flat, 2=OTel
}

internal sealed record InterceptCallSite
{
    public InstrumentedMethodInfo Method { get; init; } = null!;
    public int LocationVersion { get; init; }
    public string LocationData { get; init; } = "";
    public string DisplayLocation { get; init; } = "";

    public string ReceiverType { get; init; } = "";
    public bool IsStaticCall { get; init; }
}

internal sealed record ParameterInfo(
    string Name,
    string Type,
    string? RefKind,
    bool IsComplex = false,
    EquatableArray<PropertyMetadata> Properties = default,
    bool HasNoInstrument = false,
    EquatableArray<string> NoInstrumentProperties = default
) : IEquatable<ParameterInfo>;

internal sealed record PropertyMetadata(
    string Name,
    string Type
) : IEquatable<PropertyMetadata>;

internal sealed record TagMemberInfo(
    string MemberName,
    string? TagName,
    string Type,
    bool IsComplex = false,
    EquatableArray<PropertyMetadata> Properties = default,
    EquatableArray<string> Skip = default,
    EquatableArray<string> Fields = default
) : IEquatable<TagMemberInfo>;

internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly T[]? _array;
    public EquatableArray(T[] array) => _array = array;

    public ReadOnlySpan<T> AsSpan() => _array.AsSpan();
    public int Length => _array?.Length ?? 0;
    public T this[int index] => _array![index];

    public bool Equals(EquatableArray<T> other)
    {
        if (_array is null && other._array is null) return true;
        if (_array is null || other._array is null) return false;
        return _array.AsSpan().SequenceEqual(other._array.AsSpan());
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> o && Equals(o);
    public override int GetHashCode()
    {
        if (_array is null) return 0;
        unchecked
        {
            int hash = 17;
            foreach (var item in _array)
                hash = hash * 31 + (item?.GetHashCode() ?? 0);
            return hash;
        }
    }

    public IEnumerator<T> GetEnumerator() =>
        ((_array as IEnumerable<T>) ?? Array.Empty<T>()).GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
