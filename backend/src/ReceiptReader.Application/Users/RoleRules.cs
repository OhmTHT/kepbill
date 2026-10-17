using ReceiptReader.Domain.Enums;

namespace ReceiptReader.Application.Users;

/// <summary>Who may do what. Kept free of I/O so the rules can be tested directly.</summary>
public static class RoleRules
{
    /// <summary>Sees every company without being assigned to it.</summary>
    public static bool SeesAllCompanies(UserRole role) => role == UserRole.SuperAdmin;

    /// <summary>Approve documents, record payments, close periods, see the journal and outstanding.</summary>
    public static bool CanApprove(UserRole role) => role is UserRole.Approver or UserRole.Admin or UserRole.SuperAdmin;

    /// <summary>Roles <paramref name="actor"/> may give to users they create or edit.</summary>
    public static IReadOnlyList<UserRole> Assignable(UserRole actor) => actor switch
    {
        UserRole.SuperAdmin => [UserRole.User, UserRole.Approver, UserRole.Admin, UserRole.SuperAdmin],
        UserRole.Admin => [UserRole.User, UserRole.Approver],
        _ => [],
    };

    /// <summary>Whether <paramref name="actor"/> may manage a user who has <paramref name="target"/> (company scope aside).</summary>
    public static bool CanManage(UserRole actor, UserRole target) => Assignable(actor).Contains(target);

    /// <summary>
    /// A company admin edits only memberships inside their own companies: those outside are kept as they are.
    /// </summary>
    public static HashSet<Guid> MergeCompanies(IEnumerable<Guid> current, IEnumerable<Guid> requested, IReadOnlyCollection<Guid> scope) =>
        current.Where(c => !scope.Contains(c)).Concat(requested.Where(scope.Contains)).ToHashSet();
}
