#if !NET35
using System;
using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace JetBrains.Diagnostics.StringInterpolation
{
  [InterpolatedStringHandler]
  public ref struct JetDefaultInterpolatedStringHandler
  {
    private const int GuessedLengthPerHole = 11;
    private const int MinimumArrayPoolLength = 256;

    private readonly IFormatProvider? myProvider;

    private char[]? myArrayToReturnToPool;
    private Span<char> myChars;
    private int myPos;
    private readonly bool myHasCustomFormatter;

    public JetDefaultInterpolatedStringHandler(int literalLength, int formattedCount)
    {
      myProvider = null;
      myChars = myArrayToReturnToPool = ArrayPool<char>.Shared.Rent(GetDefaultLength(literalLength, formattedCount));
      myPos = 0;
      myHasCustomFormatter = false;
    }

    public JetDefaultInterpolatedStringHandler(int literalLength, int formattedCount, IFormatProvider? provider)
    {
      myProvider = provider;
      myChars = myArrayToReturnToPool = ArrayPool<char>.Shared.Rent(GetDefaultLength(literalLength, formattedCount));
      myPos = 0;
      myHasCustomFormatter = provider is not null && HasCustomFormatter(provider);
    }
    
    public JetDefaultInterpolatedStringHandler(int literalLength, int formattedCount, IFormatProvider? provider, Span<char> initialBuffer)
    {
      myProvider = provider;
      myChars = initialBuffer;
      myArrayToReturnToPool = null;
      myPos = 0;
      myHasCustomFormatter = provider is not null && HasCustomFormatter(provider);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)] // becomes a constant when inputs are constant
    internal static int GetDefaultLength(int literalLength, int formattedCount) => Math.Max(MinimumArrayPoolLength, literalLength + (formattedCount * GuessedLengthPerHole));

    public override string ToString() => Text.ToString();

    public string ToStringAndClear()
    {
      var result = Text.ToString();
      Clear();
      return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)] // used only on a few hot paths
    internal void Clear()
    {
      var toReturn = myArrayToReturnToPool;
      this = default; // defensive clear
      if (toReturn is not null)
      {
        ArrayPool<char>.Shared.Return(toReturn);
      }
    }

    internal ReadOnlySpan<char> Text => myChars.Slice(0, myPos);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AppendLiteral(string value)
    {
      if (value.Length == 1)
      {
        var chars = myChars;
        var pos = myPos;
        if ((uint)pos < (uint)chars.Length)
        {
          chars[pos] = value[0];
          myPos = pos + 1;
        }
        else
        {
          GrowThenCopyString(value);
        }

        return;
      }

      if (value.Length == 2)
      {
        var chars = myChars;
        var pos = myPos;
        if ((uint)pos < chars.Length - 1)
        {
          Unsafe.WriteUnaligned(
            ref Unsafe.As<char, byte>(ref Unsafe.Add(ref MemoryMarshal.GetReference(chars), pos)),
            Unsafe.ReadUnaligned<int>(ref Unsafe.As<char, byte>(ref MemoryMarshal.GetReference(value.AsSpan()))));
          myPos = pos + 2;
        }
        else
        {
          GrowThenCopyString(value);
        }

        return;
      }

      AppendFormatted(value.AsSpan());
    }

    #region AppendFormatted

    #region AppendFormatted T

    public void AppendFormatted<T>(T value)
    {
      AppendFormatted(value, null);
    }

    public void AppendFormatted<T>(T value, string? format)
    {
      if (myHasCustomFormatter)
      {
        AppendCustomFormatter(value, format);
        return;
      }

      var s = ToString(value, format);

      if (s is not null)
      {
        AppendFormatted(s.AsSpan());
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string? ToString<T>(T value, string? format)
    {
      return value is IFormattable formattable ? formattable.ToString(format, myProvider) : value?.ToString();
    }

    public void AppendFormatted<T>(T value, int alignment)
    {
      var startingPos = myPos;
      AppendFormatted(value);
      if (alignment != 0)
      {
        AppendOrInsertAlignmentIfNeeded(startingPos, alignment);
      }
    }

    public void AppendFormatted<T>(T value, int alignment, string? format)
    {
      var startingPos = myPos;
      AppendFormatted(value, format);
      if (alignment != 0)
      {
        AppendOrInsertAlignmentIfNeeded(startingPos, alignment);
      }
    }

    #endregion

    #region AppendFormatted ReadOnlySpan<char>

    public void AppendFormatted(ReadOnlySpan<char> value)
    {
      if (value.TryCopyTo(myChars.Slice(myPos)))
      {
        myPos += value.Length;
      }
      else
      {
        GrowThenCopySpan(value);
      }
    }

    public void AppendFormatted(ReadOnlySpan<char> value, int alignment = 0, string? format = null)
    {
      var leftAlign = false;
      if (alignment < 0)
      {
        leftAlign = true;
        alignment = -alignment;
      }

      var paddingRequired = alignment - value.Length;
      if (paddingRequired <= 0)
      {
        // The value is as large or larger than the required amount of padding,
        // so just write the value.
        AppendFormatted(value);
        return;
      }

      // Write the value along with the appropriate padding.
      EnsureCapacityForAdditionalChars(value.Length + paddingRequired);
      if (leftAlign)
      {
        value.CopyTo(myChars.Slice(myPos));
        myPos += value.Length;
        myChars.Slice(myPos, paddingRequired).Fill(' ');
        myPos += paddingRequired;
      }
      else
      {
        myChars.Slice(myPos, paddingRequired).Fill(' ');
        myPos += paddingRequired;
        value.CopyTo(myChars.Slice(myPos));
        myPos += value.Length;
      }
    }

    #endregion

    #region AppendFormatted string

    public void AppendFormatted(string? value)
    {
      if (!myHasCustomFormatter &&
          value is not null)
      {
        AppendFormatted(value.AsSpan());
      }
      else
      {
        AppendFormattedSlow(value);
      }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AppendFormattedSlow(string? value)
    {
      if (myHasCustomFormatter)
      {
        AppendCustomFormatter(value, format: null);
      }
      else if (value is not null)
      {
        AppendFormatted(value.AsSpan());
      }
    }

    public void AppendFormatted(string? value, int alignment = 0, string? format = null) =>
      AppendFormatted<string?>(value, alignment, format);

    #endregion

    #region AppendFormatted object
    public void AppendFormatted(object? value, int alignment = 0, string? format = null) =>
      AppendFormatted<object?>(value, alignment, format);

    #endregion

    #endregion

    [MethodImpl(MethodImplOptions.AggressiveInlining)] // only used in a few hot path call sites
    internal static bool HasCustomFormatter(IFormatProvider provider)
    {
      Assertion.Assert(provider is not null, "provider is not null");
      Assertion.Assert(provider is not CultureInfo || provider.GetFormat(typeof(ICustomFormatter)) is null, "Expected CultureInfo to not provide a custom formatter");
      
      return
        provider.GetType() != typeof(CultureInfo) && // optimization to avoid GetFormat in the majority case
        provider.GetFormat(typeof(ICustomFormatter)) != null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AppendCustomFormatter<T>(T value, string? format)
    {
      Assertion.Assert(myHasCustomFormatter, "_hasCustomFormatter");
      Assertion.Assert(myProvider != null, "_provider != null");

      var formatter = (ICustomFormatter?)myProvider.GetFormat(typeof(ICustomFormatter));
      Assertion.Assert(formatter != null, "An incorrectly written provider said it implemented ICustomFormatter, and then didn't");

      if (formatter?.Format(format, value, myProvider) is string customFormatted)
      {
        AppendFormatted(customFormatted.AsSpan());
      }
    }

    private void AppendOrInsertAlignmentIfNeeded(int startingPos, int alignment)
    {
      Assertion.Assert(startingPos >= 0 && startingPos <= myPos, "startingPos >= 0 && startingPos <= _pos");
      Assertion.Assert(alignment != 0, "alignment != 0");

      var charsWritten = myPos - startingPos;

      var leftAlign = false;
      if (alignment < 0)
      {
        leftAlign = true;
        alignment = -alignment;
      }

      var paddingNeeded = alignment - charsWritten;
      if (paddingNeeded > 0)
      {
        EnsureCapacityForAdditionalChars(paddingNeeded);

        if (leftAlign)
        {
          myChars.Slice(myPos, paddingNeeded).Fill(' ');
        }
        else
        {
          myChars.Slice(startingPos, charsWritten).CopyTo(myChars.Slice(startingPos + paddingNeeded));
          myChars.Slice(startingPos, paddingNeeded).Fill(' ');
        }

        myPos += paddingNeeded;
      }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureCapacityForAdditionalChars(int additionalChars)
    {
      if (myChars.Length - myPos < additionalChars)
      {
        Grow(additionalChars);
      }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void GrowThenCopyString(string value)
    {
      Grow(value.Length);
      value.AsSpan().CopyTo(myChars.Slice(myPos));
      myPos += value.Length;
    }
    
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void GrowThenCopySpan(ReadOnlySpan<char> value)
    {
      Grow(value.Length);
      value.CopyTo(myChars.Slice(myPos));
      myPos += value.Length;
    }

    [MethodImpl(MethodImplOptions.NoInlining)] // keep consumers as streamlined as possible
    private void Grow(int additionalChars)
    {
      Assertion.Assert(additionalChars > myChars.Length - myPos, "additionalChars > _chars.Length - _pos");
      GrowCore((uint)myPos + (uint)additionalChars);
    }

    [MethodImpl(MethodImplOptions.NoInlining)] // keep consumers as streamlined as possible
    private void Grow()
    {
      GrowCore((uint)myChars.Length + 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void GrowCore(uint requiredMinCapacity)
    {
      var newCapacity = Math.Max(requiredMinCapacity, Math.Min((uint)myChars.Length * 2, int.MaxValue / sizeof(char)));
      var arraySize = (int)Clamp(newCapacity, MinimumArrayPoolLength, int.MaxValue);

      var newArray = ArrayPool<char>.Shared.Rent(arraySize);
      myChars.Slice(0, myPos).CopyTo(newArray);

      var toReturn = myArrayToReturnToPool;
      myChars = myArrayToReturnToPool = newArray;

      if (toReturn is not null)
      {
        ArrayPool<char>.Shared.Return(toReturn);
      }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Clamp(uint value, uint min, uint max)
    {
      if (min > max)
        throw new ArgumentException($"{nameof(max)}:{max} must be more or equal than {nameof(min)}:{min}");

      if (value < min)
        return min;
      
      if (value > max)
        return max;

      return value;
    }
  }
}
#endif