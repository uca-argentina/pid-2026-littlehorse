using DrinkIt.Domain.Staff;

namespace DrinkIt.Domain.Tests.Staff;

/// <summary>
/// The three roles a venue can hand out. There is no "bartender": the KDS is the
/// bar station's own account, shared by everyone preparing there, because the
/// tablet belongs to the station and not to a person (functional design, §11).
/// </summary>
public class StaffRoleTests
{
    public static TheoryData<StaffRole> EveryRole() => [.. Enum.GetValues<StaffRole>()];

    [Theory]
    [MemberData(nameof(EveryRole))]
    public void Create_WhenRoleIsOneTheVenueCanHandOut_Succeeds(StaffRole role)
    {
        StaffUser user = StaffUser.Create(Guid.CreateVersion7(), "euge", "hash", role);

        Assert.Equal(role, user.Role);
    }

    /// <summary>
    /// The rename from Bartender to Kds kept the number, so rows written before
    /// it still read back as the same role and no data migration is needed.
    /// </summary>
    [Theory]
    [InlineData(StaffRole.Administrator, 1)]
    [InlineData(StaffRole.Kds, 2)]
    [InlineData(StaffRole.Waiter, 3)]
    public void Role_WhenStored_KeepsItsNumber(StaffRole role, int stored) =>
        Assert.Equal(stored, (int)role);
}
