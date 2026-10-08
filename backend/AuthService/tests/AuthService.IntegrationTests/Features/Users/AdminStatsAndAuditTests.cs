using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Contracts.Admin;
using AuthService.Core.Features.Admin.Audit;
using AuthService.Domain;
using AuthService.Domain.AdminAuditLog;
using AuthService.Infrastructure.Postgres;
using AuthService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AuthService.IntegrationTests.Features.Users;

[Collection(nameof(IntegrationTestFixture))]
public class AdminStatsAndAuditTests : IntegrationTestsBase
{
    public AdminStatsAndAuditTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    // ==========================================================================
    // GET /users/admin/stats
    // ==========================================================================

    [Fact]
    public async Task GetAdminStats_AnonymousRequest_ShouldReturnUnauthorized()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.GetAsync("/users/admin/stats");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAdminStats_WithoutPermission_ShouldReturnForbidden()
    {
        AuthorizeAs(Guid.NewGuid(), "Student", "student@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.GetAsync("/users/admin/stats");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetAdminStats_AsAdmin_ShouldReturnCounts()
    {
        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "admin@test.com", "platform-admin");
        AuthorizeAs(adminId, "Admin", "admin@test.com", "platform-admin");

        // Three additional users
        await SeedUserAsync(Guid.NewGuid(), "U1", "u1@test.com", "platform-participant");
        await SeedUserAsync(Guid.NewGuid(), "U2", "u2@test.com", "platform-participant");
        await SeedUserAsync(Guid.NewGuid(), "U3", "u3@test.com", "platform-author");

        // Mark one user as recently active (last_login_at within last week)
        await ExecuteInDb(async db =>
        {
            Account? u3 = await db.Users.SingleAsync(u => u.Email == "u3@test.com");
            u3.LastLoginAt = DateTime.UtcNow.AddHours(-1);
            await db.SaveChangesAsync();
        });

        HttpResponseMessage response = await HttpClient.GetAsync("/users/admin/stats");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        using JsonDocument doc = JsonDocument.Parse(body);
        JsonElement root = doc.RootElement.GetProperty("result");

        Assert.Equal(4, root.GetProperty("totalUsers").GetInt64());
        Assert.Equal(4, root.GetProperty("newToday").GetInt64());
        Assert.Equal(1, root.GetProperty("activeThisWeek").GetInt64());
        Assert.Contains(root.GetProperty("byRole").EnumerateObject(), p => p.Name == "platform-admin");
    }

    // ==========================================================================
    // GET /users/admin/audit-log
    // ==========================================================================

    [Fact]
    public async Task GetAuditLog_AsAdmin_ReturnsPaginatedEntries()
    {
        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "admin@test.com", "platform-admin");
        AuthorizeAs(adminId, "Admin", "admin@test.com", "platform-admin");

        Guid targetUserId = Guid.NewGuid();
        await SeedUserAsync(targetUserId, "Target", "target@test.com", "platform-participant");

        // Trigger an audited admin action: set lockout
        HttpResponseMessage lockoutResp = await HttpClient.PostAsJsonAsync(
            $"/users/{targetUserId}/lockout",
            new { isLocked = true });
        Assert.True(lockoutResp.IsSuccessStatusCode, await lockoutResp.Content.ReadAsStringAsync());

        // The audit-log endpoint reads from the auth.admin_audit_log table populated by the filter
        HttpResponseMessage response = await HttpClient.GetAsync(
            $"/users/admin/audit-log?targetUserId={targetUserId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        using JsonDocument doc = JsonDocument.Parse(body);
        JsonElement items = doc.RootElement.GetProperty("result").GetProperty("items");

        Assert.NotEqual(0, items.GetArrayLength());
        JsonElement first = items[0];
        Assert.Equal("users.lockout.set", first.GetProperty("action").GetString());
        Assert.Equal("success", first.GetProperty("result").GetString());
        Assert.Equal(adminId, first.GetProperty("adminId").GetGuid());
        Assert.Equal(targetUserId, first.GetProperty("targetUserId").GetGuid());
    }

    [Fact]
    public async Task GetAuditLog_FailureCase_RecordsFailure()
    {
        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "admin@test.com", "platform-admin");
        AuthorizeAs(adminId, "Admin", "admin@test.com", "platform-admin");

        // Trigger an admin action that should fail: delete non-existent user
        Guid bogusId = Guid.NewGuid();
        HttpResponseMessage delResp = await HttpClient.DeleteAsync($"/users/{bogusId}");
        Assert.Equal(HttpStatusCode.NotFound, delResp.StatusCode);

        HttpResponseMessage response = await HttpClient.GetAsync(
            $"/users/admin/audit-log?adminId={adminId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        using JsonDocument doc = JsonDocument.Parse(body);
        JsonElement items = doc.RootElement.GetProperty("result").GetProperty("items");

        Assert.Contains(items.EnumerateArray(), it =>
            it.GetProperty("action").GetString() == "users.deleted" &&
            it.GetProperty("result").GetString() == "failure");
    }

    [Fact]
    public async Task GetAuditLog_WithoutManagePermission_ShouldReturnForbidden()
    {
        // VIEW is not enough — audit-log requires MANAGE
        AuthorizeAs(Guid.NewGuid(), "Moderator", "mod@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.GetAsync("/users/admin/audit-log");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ==========================================================================
    // admin_audit_log table direct check — verifies filter writes rows
    // ==========================================================================

    [Fact]
    public async Task AdminMutation_WritesAuditRow()
    {
        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "admin@test.com", "platform-admin");
        AuthorizeAs(adminId, "Admin", "admin@test.com", "platform-admin");

        Guid targetUserId = Guid.NewGuid();
        await SeedUserAsync(targetUserId, "Target", "target-row@test.com", "platform-participant");

        HttpResponseMessage resp = await HttpClient.PostAsJsonAsync(
            $"/users/{targetUserId}/lockout",
            new { isLocked = true });
        Assert.True(resp.IsSuccessStatusCode);

        await ExecuteInDb(async db =>
        {
            List<AdminAuditLogEntry> rows = await db.AdminAuditLogs
                .Where(x => x.AdminId == adminId && x.TargetUserId == targetUserId)
                .ToListAsync();

            Assert.Single(rows);
            Assert.Equal("users.lockout.set", rows[0].Action);
            Assert.Equal("POST", rows[0].Method);
            Assert.Equal("success", rows[0].Result);
            Assert.NotNull(rows[0].PayloadJson);
        });
    }

    [Fact]
    public async Task AdminPasswordMutations_ShouldRedactPasswordsFromAuditPayload()
    {
        const string createPassword = "CreateSecret123!";
        const string resetPassword = "ResetSecret123!";

        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "admin-password-audit@test.com", "platform-admin");
        await SeedRolesAsync("platform-participant");
        AuthorizeAs(adminId, "Admin", "admin-password-audit@test.com", "platform-admin");

        HttpResponseMessage createResponse = await HttpClient.PostAsJsonAsync(
            "/users/",
            new AdminCreateUserRequest(
                "audit-created@test.com",
                "audit-created",
                createPassword,
                ["platform-participant"]));
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        Guid targetUserId = Guid.NewGuid();
        await SeedUserAsync(targetUserId, "Target", "audit-target@test.com", "platform-participant");

        HttpResponseMessage resetResponse = await HttpClient.PostAsJsonAsync(
            $"/users/{targetUserId}/password",
            new AdminSetPasswordRequest(resetPassword));
        Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);

        await ExecuteInDb(async db =>
        {
            List<AdminAuditLogEntry> rows = await db.AdminAuditLogs
                .Where(x => x.AdminId == adminId &&
                            (x.Action == AdminAuditAction.UserCreated ||
                             x.Action == AdminAuditAction.UserPasswordSet))
                .ToListAsync();

            Assert.Equal(2, rows.Count);
            Assert.All(rows, row =>
            {
                Assert.NotNull(row.PayloadJson);
                Assert.Contains("[REDACTED]", row.PayloadJson, StringComparison.Ordinal);
                Assert.DoesNotContain(createPassword, row.PayloadJson, StringComparison.Ordinal);
                Assert.DoesNotContain(resetPassword, row.PayloadJson, StringComparison.Ordinal);
            });
        });
    }
}
