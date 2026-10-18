using System.Reflection;
using Hadron.Erp.Api.Platform.Security;

namespace Hadron.Erp.IntegrationTests;

public sealed class PermissionCatalogContractTests
{
    [Fact]
    public void Every_immutable_permission_constant_is_registered_once_with_complete_admin_metadata()
    {
        var declared = typeof(Permissions).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            .Select(field => Assert.IsType<string>(field.GetRawConstantValue()))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var registered = PermissionCatalog.All.Select(permission => permission.Code)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(declared, registered);
        Assert.Equal(registered.Length, registered.Distinct(StringComparer.Ordinal).Count());
        Assert.All(PermissionCatalog.All, permission =>
        {
            Assert.Matches("^[a-z0-9_.]+$", permission.Code);
            Assert.Matches("^[A-Z0-9_]+$", permission.Area);
            Assert.False(string.IsNullOrWhiteSpace(permission.NameFa), permission.Code);
            Assert.False(string.IsNullOrWhiteSpace(permission.NameEn), permission.Code);
        });
    }
}
