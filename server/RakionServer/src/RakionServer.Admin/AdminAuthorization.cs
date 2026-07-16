namespace RakionServer.Admin;

public enum AdminRole
{
    Viewer,
    Operator,
    Owner
}

public enum AdminPermission
{
    AccountCreate,
    AccountSecurityWrite,
    EconomyWrite,
    InventoryWrite,
    ClanWrite,
    ConfigurationWrite,
    UpdateWrite
}

public sealed record AdminIdentity(string OperatorId, AdminRole Role)
{
    public static AdminIdentity FromConfiguration(IConfiguration configuration)
    {
        string operatorId = configuration["Admin:User"]?.Trim() ?? "admin";
        if (operatorId.Length is < 1 or > 64)
            throw new InvalidOperationException("Admin__User deve ter entre 1 e 64 caracteres.");

        string configuredRole = configuration["Admin:Role"] ?? nameof(AdminRole.Owner);
        if (!Enum.TryParse(configuredRole, true, out AdminRole role))
            throw new InvalidOperationException("Admin__Role deve ser Viewer, Operator ou Owner.");
        return new AdminIdentity(operatorId, role);
    }

    public bool IsAllowed(AdminPermission permission) => Role switch
    {
        AdminRole.Owner => true,
        AdminRole.Operator => permission is AdminPermission.AccountSecurityWrite
            or AdminPermission.EconomyWrite or AdminPermission.InventoryWrite,
        _ => false
    };

    public void Demand(AdminPermission permission)
    {
        if (!IsAllowed(permission))
            throw new UnauthorizedAccessException(
                $"O papel {Role} não possui a permissão {permission}.");
    }
}
