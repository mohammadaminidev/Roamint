namespace Roamint.SharedKernel;

/// <summary>Raised when a domain invariant is violated.</summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }

    public DomainException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Raised when an operation combines two <see cref="Money"/> values of different currencies.</summary>
public sealed class CurrencyMismatchException : DomainException
{
    public CurrencyMismatchException(Currency left, Currency right)
        : base($"Cannot operate on mismatched currencies {left.Code} and {right.Code}.")
    {
        Left = left;
        Right = right;
    }

    public Currency Left { get; }

    public Currency Right { get; }
}
