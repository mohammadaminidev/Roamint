using Roamint.SharedKernel;

namespace Roamint.Expenses.Domain.Tests;

public class CurrencyTests
{
    [Theory]
    [InlineData("USD")]
    [InlineData("usd")]
    [InlineData(" Amd ")]
    [InlineData("USDT")]
    [InlineData("IRT")]
    public void FromCode_resolves_supported_codes(string code)
    {
        Currency.FromCode(code).Code.ShouldBe(code.Trim().ToUpperInvariant());
    }

    [Fact]
    public void FromCode_throws_on_unsupported_code()
    {
        Should.Throw<DomainException>(() => Currency.FromCode("EUR"));
    }
}
