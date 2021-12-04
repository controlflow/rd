#if !NET35
using System;
using System.Runtime.CompilerServices;

namespace JetBrains.Diagnostics.StringInterpolation;

[InterpolatedStringHandler]
public ref struct JetNotNullConditionInterpolatedStringHandler
{
  private JetConditionInterpolatedStringHandler myHandler;

  public bool IsEnabled
  {
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    get => myHandler.IsEnabled;
  }

  public JetNotNullConditionInterpolatedStringHandler(
    int literalLength,
    int formattedCount,
    object? obj,
    out bool isEnabled)
  {
    myHandler = new JetConditionInterpolatedStringHandler(literalLength, formattedCount, obj != null , out isEnabled);
  }
  
  public override string ToString() => IsEnabled ? myHandler.ToString() : "";
  
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public string ToStringAndClear() => IsEnabled ? myHandler.ToStringAndClear() : "";

  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void AppendLiteral(string value) => myHandler.AppendLiteral(value);
  
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void AppendFormatted<T>(T value) => myHandler.AppendFormatted(value);
  
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void AppendFormatted<T>(T value, string? format) => myHandler.AppendFormatted(value, format);
  
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void AppendFormatted<T>(T value, int alignment) => myHandler.AppendFormatted(value, alignment);
  
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void AppendFormatted<T>(T value, int alignment, string? format) => myHandler.AppendFormatted(value, alignment, format);
  
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void AppendFormatted(ReadOnlySpan<char> value) => myHandler.AppendFormatted(value);
  
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void AppendFormatted(ReadOnlySpan<char> value, int alignment = 0, string? format = null) => myHandler.AppendFormatted(value, alignment, format);
  
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void AppendFormatted(string? value) => myHandler.AppendFormatted(value);
  
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void AppendFormatted(string? value, int alignment = 0, string? format = null) => myHandler.AppendFormatted(value, alignment, format);
  
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public void AppendFormatted(object? value, int alignment = 0, string? format = null) => myHandler.AppendFormatted(value, alignment, format);
}
#endif