namespace Lantern.Api.Keycloak;

public sealed record KcUser(string Id, string Username, string? FirstName, string? LastName, string? Email, bool Enabled);
public sealed record KcGroup(string Id, string Name, string Path);
public sealed record KcRole(string Name);

public sealed record StaffMember(string Id, string Username, string Name, string? Email, bool Enabled,
    IReadOnlyList<string> Groups, IReadOnlyList<string> Roles);

/// <summary>Outcome of creating a staff member. Errors holds Keycloak's validation messages (400).</summary>
public sealed record CreateStaffResult(string? Id, bool Conflict, Dictionary<string, string[]>? Errors, bool InviteSent);

public sealed record NewStaff(string? Username, string? FirstName, string? LastName, string? Email, string[]? Departments);
public sealed record DepartmentsUpdate(string[]? Departments);
