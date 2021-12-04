// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices
{
  /// <summary>Indicates which arguments to a method involving an interpolated string handler should be passed to that handler.</summary>
  [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
  internal sealed class InterpolatedStringHandlerArgumentAttribute : Attribute
  {
    /// <summary>Initializes a new instance of the <see cref="InterpolatedStringHandlerArgumentAttribute"/> class.</summary>
    /// <param name="argument">The name of the argument that should be passed to the handler.</param>
    /// <remarks><see langword="null"/> may be used as the name of the receiver in an instance method.</remarks>
    public InterpolatedStringHandlerArgumentAttribute(string argument) => Arguments = new string[] { argument };

    /// <summary>Initializes a new instance of the <see cref="InterpolatedStringHandlerArgumentAttribute"/> class.</summary>
    /// <param name="arguments">The names of the arguments that should be passed to the handler.</param>
    /// <remarks><see langword="null"/> may be used as the name of the receiver in an instance method.</remarks>
    public InterpolatedStringHandlerArgumentAttribute(params string[] arguments) => Arguments = arguments;

    /// <summary>Gets the names of the arguments that should be passed to the handler.</summary>
    /// <remarks><see langword="null"/> may be used as the name of the receiver in an instance method.</remarks>
    public string[] Arguments { get; }
  }
  
  /// <summary>Indicates the attributed type is to be used as an interpolated string handler.</summary>
  [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
  internal sealed class InterpolatedStringHandlerAttribute : Attribute
  {
    /// <summary>Initializes the <see cref="InterpolatedStringHandlerAttribute"/>.</summary>
    public InterpolatedStringHandlerAttribute() { }
  }
  
  [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
  internal sealed class CallerArgumentExpressionAttribute : Attribute
  {
    public CallerArgumentExpressionAttribute(string parameterName)
    {
      ParameterName = parameterName;
    }

    public string ParameterName { get; }
  }
}