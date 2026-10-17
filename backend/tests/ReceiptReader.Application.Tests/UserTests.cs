using ReceiptReader.Application.Abstractions;
using ReceiptReader.Application.Users;
using ReceiptReader.Domain.Enums;

namespace ReceiptReader.Application.Tests;

public class UserInputTests
{
    [Theory]
    [InlineData("")]
    [InlineData("1234567")]
    [InlineData(null)]
    public void Short_password_is_rejected(string? password) =>
        Assert.Throws<BadRequestException>(() => UserService.RequirePassword(password));

    [Fact]
    public void Password_length_limits()
    {
        Assert.Equal("12345678", UserService.RequirePassword("12345678"));
        Assert.Throws<BadRequestException>(() => UserService.RequirePassword(new string('x', 129)));
    }

    [Theory]
    [InlineData("  สมชาย   ใจดี ", "สมชาย ใจดี")] // shown as typed, minus extra spaces
    [InlineData("Ohm\tTH", "Ohm TH")]
    public void Name_is_cleaned_but_keeps_its_case(string input, string expected) =>
        Assert.Equal(expected, UserService.RequireName(input));

    [Theory]
    [InlineData("Ohm  TH", "ohm th")]
    [InlineData(" ADMIN ", "admin")]
    [InlineData("สมชาย", "สมชาย")]
    public void Names_compare_ignoring_case_and_spacing(string input, string expected) =>
        Assert.Equal(expected, UserService.NameKey(input));

    [Fact]
    public void Name_max_length_is_100() =>
        Assert.Throws<BadRequestException>(() => UserService.RequireName(new string('ก', 101)));

    [Theory]
    [InlineData("   ")]
    [InlineData(null)]
    public void Blank_name_is_rejected(string? name) =>
        Assert.Throws<BadRequestException>(() => UserService.RequireName(name));
}

public class RoleRulesTests
{
    [Fact]
    public void OnlySuperAdminsSeeEveryCompany()
    {
        Assert.True(RoleRules.SeesAllCompanies(UserRole.SuperAdmin));
        Assert.False(RoleRules.SeesAllCompanies(UserRole.Admin));
        Assert.False(RoleRules.SeesAllCompanies(UserRole.Approver));
    }

    [Fact]
    public void UsersCannotApprove()
    {
        Assert.False(RoleRules.CanApprove(UserRole.User));
        Assert.True(RoleRules.CanApprove(UserRole.Approver));
        Assert.True(RoleRules.CanApprove(UserRole.Admin));
        Assert.True(RoleRules.CanApprove(UserRole.SuperAdmin));
    }

    [Fact]
    public void AdminsOnlyHandOutApproverOrUser()
    {
        Assert.Equal([UserRole.User, UserRole.Approver], RoleRules.Assignable(UserRole.Admin));
        Assert.False(RoleRules.CanManage(UserRole.Admin, UserRole.Admin));
        Assert.False(RoleRules.CanManage(UserRole.Admin, UserRole.SuperAdmin));
        Assert.True(RoleRules.CanManage(UserRole.SuperAdmin, UserRole.Admin));
        Assert.Empty(RoleRules.Assignable(UserRole.Approver));
        Assert.Empty(RoleRules.Assignable(UserRole.User));
    }

    [Fact]
    public void AdminChangesOnlyTheirOwnCompanies()
    {
        Guid mine = Guid.NewGuid(), alsoMine = Guid.NewGuid(), theirs = Guid.NewGuid();
        // The user is in "mine" and in a company the admin doesn't run; the admin swaps "mine" for "alsoMine".
        var merged = RoleRules.MergeCompanies([mine, theirs], [alsoMine], [mine, alsoMine]);
        Assert.Equal(new HashSet<Guid> { theirs, alsoMine }, merged);
    }
}
