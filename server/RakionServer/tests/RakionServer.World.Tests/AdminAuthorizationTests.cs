using System;
using System.Threading.Tasks;
using RakionServer.Admin;
using Xunit;

namespace RakionServer.World.Tests;

public sealed class AdminAuthorizationTests
{
    [Theory]
    [InlineData(AdminRole.Viewer, AdminPermission.AccountSecurityWrite, false)]
    [InlineData(AdminRole.Operator, AdminPermission.AccountSecurityWrite, true)]
    [InlineData(AdminRole.Operator, AdminPermission.ConfigurationWrite, false)]
    [InlineData(AdminRole.Operator, AdminPermission.ClanWrite, false)]
    [InlineData(AdminRole.Owner, AdminPermission.ConfigurationWrite, true)]
    [InlineData(AdminRole.Owner, AdminPermission.ClanWrite, true)]
    [InlineData(AdminRole.Owner, AdminPermission.UpdateWrite, true)]
    public void RoleMatrixIsExplicit(
        AdminRole role, AdminPermission permission, bool expected)
    {
        var identity = new AdminIdentity("operator", role);
        Assert.Equal(expected, identity.IsAllowed(permission));
    }

    [Fact]
    public void DemandRejectsMissingPermission()
    {
        var identity = new AdminIdentity("viewer", AdminRole.Viewer);
        Assert.Throws<UnauthorizedAccessException>(() =>
            identity.Demand(AdminPermission.EconomyWrite));
    }

    [Theory]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("http://localhost:8080")]
    [InlineData("https://admin.example.test")]
    public void EndpointPolicyAcceptsLocalHttpOrAnyHttps(string value) =>
        Assert.Equal(value.TrimEnd('/'),
            AdminEndpointPolicy.Validate(value).ToString().TrimEnd('/'));

    [Fact]
    public void EndpointPolicyRejectsExternalPlainHttp() =>
        Assert.Throws<InvalidOperationException>(() =>
            AdminEndpointPolicy.Validate("http://0.0.0.0:8080"));

    [Fact]
    public async Task DatabaseMutationRejectsViewerBeforeIo()
    {
        var database = new AdminDb("Server=invalid",
            new AdminIdentity("viewer", AdminRole.Viewer));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            database.SetBanAsync("target", true, "ticket 123"));
    }

    [Fact]
    public async Task ClanMutationAuthorizesBeforeValidatingTarget()
    {
        var database = new AdminDb("Server=invalid",
            new AdminIdentity("operator", AdminRole.Operator));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            database.AddClanMemberAsync(1, "invalid account", "ticket 123"));
    }

    [Fact]
    public async Task AuditedMutationRequiresReasonBeforeIo()
    {
        var database = new AdminDb("Server=invalid",
            new AdminIdentity("owner", AdminRole.Owner));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            database.SetBanAsync("target", true, ""));
    }
}
