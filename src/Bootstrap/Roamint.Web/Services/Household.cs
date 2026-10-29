using System.Globalization;
using Microsoft.Extensions.Options;

namespace Roamint.Web.Services;

/// <summary>The people who pay (0024), bound from <c>Household:Members</c> in user-secrets — configuration, not
/// data: two people don't warrant an aggregate. <see cref="HouseholdMember.Key"/> is what an expense's
/// <c>PaidBy</c> stores.</summary>
public sealed class HouseholdOptions
{
    public IList<HouseholdMember> Members { get; } = [];
}

public sealed class HouseholdMember
{
    /// <summary>Stable short key stored on expenses, e.g. <c>"sara"</c>. Never rename once used.</summary>
    public string Key { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>The member's Telegram user id, so a group message maps to its payer. Null = not on Telegram.</summary>
    public long? TelegramUserId { get; set; }
}

/// <summary>Read side over <see cref="HouseholdOptions"/>: the payer picker's list, the Telegram sender lookup,
/// and display names for the summary. Members without a key are ignored.</summary>
public sealed class Household(IOptions<HouseholdOptions> options)
{
    public IReadOnlyList<HouseholdMember> Members { get; } =
        options.Value.Members.Where(member => !string.IsNullOrWhiteSpace(member.Key)).ToList();

    public HouseholdMember? FindByTelegramUser(string? senderId) =>
        long.TryParse(senderId, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var id)
            ? Members.FirstOrDefault(member => member.TelegramUserId == id)
            : null;

    /// <summary>The member's display name; an unknown key shows as itself, null as "Unattributed".</summary>
    public string DisplayNameFor(string? key)
    {
        if (key is null)
        {
            return "Unattributed";
        }

        var member = Members.FirstOrDefault(m => m.Key == key);
        return member is null || string.IsNullOrWhiteSpace(member.DisplayName) ? key : member.DisplayName;
    }
}
