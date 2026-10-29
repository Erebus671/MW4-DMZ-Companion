namespace DmzCompanion.Core.UserState;

/// <summary>Upper bounds user counts are clamped to.</summary>
public static class UserStateLimits
{
    public const int MaxCollected = 9_999;

    public const int MaxHave = 999;

    public const int MaxComponent = 9_999;

    public const long MaxCash = 9_999_999;
}
