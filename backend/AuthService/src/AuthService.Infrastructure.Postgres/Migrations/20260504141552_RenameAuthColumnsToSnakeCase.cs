using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthService.Infrastructure.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class RenameAuthColumnsToSnakeCase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OpenIddictAuthorizations_OpenIddictApplications_Application~",
                schema: "auth",
                table: "OpenIddictAuthorizations");

            migrationBuilder.DropForeignKey(
                name: "FK_OpenIddictTokens_OpenIddictApplications_ApplicationId",
                schema: "auth",
                table: "OpenIddictTokens");

            migrationBuilder.DropForeignKey(
                name: "FK_OpenIddictTokens_OpenIddictAuthorizations_AuthorizationId",
                schema: "auth",
                table: "OpenIddictTokens");

            migrationBuilder.DropForeignKey(
                name: "FK_role_claims_roles_RoleId",
                schema: "auth",
                table: "role_claims");

            migrationBuilder.DropForeignKey(
                name: "FK_user_claims_users_UserId",
                schema: "auth",
                table: "user_claims");

            migrationBuilder.DropForeignKey(
                name: "FK_user_logins_users_UserId",
                schema: "auth",
                table: "user_logins");

            migrationBuilder.DropForeignKey(
                name: "FK_user_roles_roles_RoleId",
                schema: "auth",
                table: "user_roles");

            migrationBuilder.DropForeignKey(
                name: "FK_user_roles_users_UserId",
                schema: "auth",
                table: "user_roles");

            migrationBuilder.DropForeignKey(
                name: "FK_user_tokens_users_UserId",
                schema: "auth",
                table: "user_tokens");

            migrationBuilder.DropPrimaryKey(
                name: "PK_OpenIddictTokens",
                schema: "auth",
                table: "OpenIddictTokens");

            migrationBuilder.DropPrimaryKey(
                name: "PK_OpenIddictScopes",
                schema: "auth",
                table: "OpenIddictScopes");

            migrationBuilder.DropPrimaryKey(
                name: "PK_OpenIddictAuthorizations",
                schema: "auth",
                table: "OpenIddictAuthorizations");

            migrationBuilder.DropPrimaryKey(
                name: "PK_OpenIddictApplications",
                schema: "auth",
                table: "OpenIddictApplications");

            migrationBuilder.RenameTable(
                name: "OpenIddictTokens",
                schema: "auth",
                newName: "openiddict_tokens",
                newSchema: "auth");

            migrationBuilder.RenameTable(
                name: "OpenIddictScopes",
                schema: "auth",
                newName: "openiddict_scopes",
                newSchema: "auth");

            migrationBuilder.RenameTable(
                name: "OpenIddictAuthorizations",
                schema: "auth",
                newName: "openiddict_authorizations",
                newSchema: "auth");

            migrationBuilder.RenameTable(
                name: "OpenIddictApplications",
                schema: "auth",
                newName: "openiddict_applications",
                newSchema: "auth");

            migrationBuilder.RenameColumn(
                name: "Email",
                schema: "auth",
                table: "users",
                newName: "email");

            migrationBuilder.RenameColumn(
                name: "Id",
                schema: "auth",
                table: "users",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UserName",
                schema: "auth",
                table: "users",
                newName: "user_name");

            migrationBuilder.RenameColumn(
                name: "TwoFactorEnabled",
                schema: "auth",
                table: "users",
                newName: "two_factor_enabled");

            migrationBuilder.RenameColumn(
                name: "SecurityStamp",
                schema: "auth",
                table: "users",
                newName: "security_stamp");

            migrationBuilder.RenameColumn(
                name: "PhoneNumberConfirmed",
                schema: "auth",
                table: "users",
                newName: "phone_number_confirmed");

            migrationBuilder.RenameColumn(
                name: "PhoneNumber",
                schema: "auth",
                table: "users",
                newName: "phone_number");

            migrationBuilder.RenameColumn(
                name: "PasswordHash",
                schema: "auth",
                table: "users",
                newName: "password_hash");

            migrationBuilder.RenameColumn(
                name: "NormalizedUserName",
                schema: "auth",
                table: "users",
                newName: "normalized_user_name");

            migrationBuilder.RenameColumn(
                name: "NormalizedEmail",
                schema: "auth",
                table: "users",
                newName: "normalized_email");

            migrationBuilder.RenameColumn(
                name: "LockoutEnd",
                schema: "auth",
                table: "users",
                newName: "lockout_end");

            migrationBuilder.RenameColumn(
                name: "LockoutEnabled",
                schema: "auth",
                table: "users",
                newName: "lockout_enabled");

            migrationBuilder.RenameColumn(
                name: "EmailConfirmed",
                schema: "auth",
                table: "users",
                newName: "email_confirmed");

            migrationBuilder.RenameColumn(
                name: "DisplayName",
                schema: "auth",
                table: "users",
                newName: "display_name");

            migrationBuilder.RenameColumn(
                name: "ConcurrencyStamp",
                schema: "auth",
                table: "users",
                newName: "concurrency_stamp");

            migrationBuilder.RenameColumn(
                name: "AccessFailedCount",
                schema: "auth",
                table: "users",
                newName: "access_failed_count");

            migrationBuilder.RenameColumn(
                name: "Value",
                schema: "auth",
                table: "user_tokens",
                newName: "value");

            migrationBuilder.RenameColumn(
                name: "Name",
                schema: "auth",
                table: "user_tokens",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "LoginProvider",
                schema: "auth",
                table: "user_tokens",
                newName: "login_provider");

            migrationBuilder.RenameColumn(
                name: "UserId",
                schema: "auth",
                table: "user_tokens",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "RoleId",
                schema: "auth",
                table: "user_roles",
                newName: "role_id");

            migrationBuilder.RenameColumn(
                name: "UserId",
                schema: "auth",
                table: "user_roles",
                newName: "user_id");

            migrationBuilder.RenameIndex(
                name: "IX_user_roles_RoleId",
                schema: "auth",
                table: "user_roles",
                newName: "IX_user_roles_role_id");

            migrationBuilder.RenameColumn(
                name: "UserId",
                schema: "auth",
                table: "user_logins",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "ProviderDisplayName",
                schema: "auth",
                table: "user_logins",
                newName: "provider_display_name");

            migrationBuilder.RenameColumn(
                name: "ProviderKey",
                schema: "auth",
                table: "user_logins",
                newName: "provider_key");

            migrationBuilder.RenameColumn(
                name: "LoginProvider",
                schema: "auth",
                table: "user_logins",
                newName: "login_provider");

            migrationBuilder.RenameIndex(
                name: "IX_user_logins_UserId",
                schema: "auth",
                table: "user_logins",
                newName: "IX_user_logins_user_id");

            migrationBuilder.RenameColumn(
                name: "Id",
                schema: "auth",
                table: "user_claims",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UserId",
                schema: "auth",
                table: "user_claims",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "ClaimValue",
                schema: "auth",
                table: "user_claims",
                newName: "claim_value");

            migrationBuilder.RenameColumn(
                name: "ClaimType",
                schema: "auth",
                table: "user_claims",
                newName: "claim_type");

            migrationBuilder.RenameIndex(
                name: "IX_user_claims_UserId",
                schema: "auth",
                table: "user_claims",
                newName: "IX_user_claims_user_id");

            migrationBuilder.RenameColumn(
                name: "Name",
                schema: "auth",
                table: "roles",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Id",
                schema: "auth",
                table: "roles",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "NormalizedName",
                schema: "auth",
                table: "roles",
                newName: "normalized_name");

            migrationBuilder.RenameColumn(
                name: "ConcurrencyStamp",
                schema: "auth",
                table: "roles",
                newName: "concurrency_stamp");

            migrationBuilder.RenameColumn(
                name: "Id",
                schema: "auth",
                table: "role_claims",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "RoleId",
                schema: "auth",
                table: "role_claims",
                newName: "role_id");

            migrationBuilder.RenameColumn(
                name: "ClaimValue",
                schema: "auth",
                table: "role_claims",
                newName: "claim_value");

            migrationBuilder.RenameColumn(
                name: "ClaimType",
                schema: "auth",
                table: "role_claims",
                newName: "claim_type");

            migrationBuilder.RenameIndex(
                name: "IX_role_claims_RoleId",
                schema: "auth",
                table: "role_claims",
                newName: "IX_role_claims_role_id");

            migrationBuilder.RenameColumn(
                name: "Type",
                schema: "auth",
                table: "openiddict_tokens",
                newName: "type");

            migrationBuilder.RenameColumn(
                name: "Subject",
                schema: "auth",
                table: "openiddict_tokens",
                newName: "subject");

            migrationBuilder.RenameColumn(
                name: "Status",
                schema: "auth",
                table: "openiddict_tokens",
                newName: "status");

            migrationBuilder.RenameColumn(
                name: "Properties",
                schema: "auth",
                table: "openiddict_tokens",
                newName: "properties");

            migrationBuilder.RenameColumn(
                name: "Payload",
                schema: "auth",
                table: "openiddict_tokens",
                newName: "payload");

            migrationBuilder.RenameColumn(
                name: "Id",
                schema: "auth",
                table: "openiddict_tokens",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "ReferenceId",
                schema: "auth",
                table: "openiddict_tokens",
                newName: "reference_id");

            migrationBuilder.RenameColumn(
                name: "RedemptionDate",
                schema: "auth",
                table: "openiddict_tokens",
                newName: "redemption_date");

            migrationBuilder.RenameColumn(
                name: "ExpirationDate",
                schema: "auth",
                table: "openiddict_tokens",
                newName: "expiration_date");

            migrationBuilder.RenameColumn(
                name: "CreationDate",
                schema: "auth",
                table: "openiddict_tokens",
                newName: "creation_date");

            migrationBuilder.RenameColumn(
                name: "ConcurrencyToken",
                schema: "auth",
                table: "openiddict_tokens",
                newName: "concurrency_token");

            migrationBuilder.RenameColumn(
                name: "AuthorizationId",
                schema: "auth",
                table: "openiddict_tokens",
                newName: "authorization_id");

            migrationBuilder.RenameColumn(
                name: "ApplicationId",
                schema: "auth",
                table: "openiddict_tokens",
                newName: "application_id");

            migrationBuilder.RenameIndex(
                name: "IX_OpenIddictTokens_ReferenceId",
                schema: "auth",
                table: "openiddict_tokens",
                newName: "IX_openiddict_tokens_reference_id");

            migrationBuilder.RenameIndex(
                name: "IX_OpenIddictTokens_AuthorizationId",
                schema: "auth",
                table: "openiddict_tokens",
                newName: "IX_openiddict_tokens_authorization_id");

            migrationBuilder.RenameIndex(
                name: "IX_OpenIddictTokens_ApplicationId_Status_Subject_Type",
                schema: "auth",
                table: "openiddict_tokens",
                newName: "IX_openiddict_tokens_application_id_status_subject_type");

            migrationBuilder.RenameColumn(
                name: "Resources",
                schema: "auth",
                table: "openiddict_scopes",
                newName: "resources");

            migrationBuilder.RenameColumn(
                name: "Properties",
                schema: "auth",
                table: "openiddict_scopes",
                newName: "properties");

            migrationBuilder.RenameColumn(
                name: "Name",
                schema: "auth",
                table: "openiddict_scopes",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Descriptions",
                schema: "auth",
                table: "openiddict_scopes",
                newName: "descriptions");

            migrationBuilder.RenameColumn(
                name: "Description",
                schema: "auth",
                table: "openiddict_scopes",
                newName: "description");

            migrationBuilder.RenameColumn(
                name: "Id",
                schema: "auth",
                table: "openiddict_scopes",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "DisplayNames",
                schema: "auth",
                table: "openiddict_scopes",
                newName: "display_names");

            migrationBuilder.RenameColumn(
                name: "DisplayName",
                schema: "auth",
                table: "openiddict_scopes",
                newName: "display_name");

            migrationBuilder.RenameColumn(
                name: "ConcurrencyToken",
                schema: "auth",
                table: "openiddict_scopes",
                newName: "concurrency_token");

            migrationBuilder.RenameIndex(
                name: "IX_OpenIddictScopes_Name",
                schema: "auth",
                table: "openiddict_scopes",
                newName: "IX_openiddict_scopes_name");

            migrationBuilder.RenameColumn(
                name: "Type",
                schema: "auth",
                table: "openiddict_authorizations",
                newName: "type");

            migrationBuilder.RenameColumn(
                name: "Subject",
                schema: "auth",
                table: "openiddict_authorizations",
                newName: "subject");

            migrationBuilder.RenameColumn(
                name: "Status",
                schema: "auth",
                table: "openiddict_authorizations",
                newName: "status");

            migrationBuilder.RenameColumn(
                name: "Scopes",
                schema: "auth",
                table: "openiddict_authorizations",
                newName: "scopes");

            migrationBuilder.RenameColumn(
                name: "Properties",
                schema: "auth",
                table: "openiddict_authorizations",
                newName: "properties");

            migrationBuilder.RenameColumn(
                name: "Id",
                schema: "auth",
                table: "openiddict_authorizations",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "CreationDate",
                schema: "auth",
                table: "openiddict_authorizations",
                newName: "creation_date");

            migrationBuilder.RenameColumn(
                name: "ConcurrencyToken",
                schema: "auth",
                table: "openiddict_authorizations",
                newName: "concurrency_token");

            migrationBuilder.RenameColumn(
                name: "ApplicationId",
                schema: "auth",
                table: "openiddict_authorizations",
                newName: "application_id");

            migrationBuilder.RenameIndex(
                name: "IX_OpenIddictAuthorizations_ApplicationId_Status_Subject_Type",
                schema: "auth",
                table: "openiddict_authorizations",
                newName: "IX_openiddict_authorizations_application_id_status_subject_type");

            migrationBuilder.RenameColumn(
                name: "Settings",
                schema: "auth",
                table: "openiddict_applications",
                newName: "settings");

            migrationBuilder.RenameColumn(
                name: "Requirements",
                schema: "auth",
                table: "openiddict_applications",
                newName: "requirements");

            migrationBuilder.RenameColumn(
                name: "Properties",
                schema: "auth",
                table: "openiddict_applications",
                newName: "properties");

            migrationBuilder.RenameColumn(
                name: "Permissions",
                schema: "auth",
                table: "openiddict_applications",
                newName: "permissions");

            migrationBuilder.RenameColumn(
                name: "Id",
                schema: "auth",
                table: "openiddict_applications",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "RedirectUris",
                schema: "auth",
                table: "openiddict_applications",
                newName: "redirect_uris");

            migrationBuilder.RenameColumn(
                name: "PostLogoutRedirectUris",
                schema: "auth",
                table: "openiddict_applications",
                newName: "post_logout_redirect_uris");

            migrationBuilder.RenameColumn(
                name: "JsonWebKeySet",
                schema: "auth",
                table: "openiddict_applications",
                newName: "json_web_key_set");

            migrationBuilder.RenameColumn(
                name: "DisplayNames",
                schema: "auth",
                table: "openiddict_applications",
                newName: "display_names");

            migrationBuilder.RenameColumn(
                name: "DisplayName",
                schema: "auth",
                table: "openiddict_applications",
                newName: "display_name");

            migrationBuilder.RenameColumn(
                name: "ConsentType",
                schema: "auth",
                table: "openiddict_applications",
                newName: "consent_type");

            migrationBuilder.RenameColumn(
                name: "ConcurrencyToken",
                schema: "auth",
                table: "openiddict_applications",
                newName: "concurrency_token");

            migrationBuilder.RenameColumn(
                name: "ClientType",
                schema: "auth",
                table: "openiddict_applications",
                newName: "client_type");

            migrationBuilder.RenameColumn(
                name: "ClientSecret",
                schema: "auth",
                table: "openiddict_applications",
                newName: "client_secret");

            migrationBuilder.RenameColumn(
                name: "ClientId",
                schema: "auth",
                table: "openiddict_applications",
                newName: "client_id");

            migrationBuilder.RenameColumn(
                name: "ApplicationType",
                schema: "auth",
                table: "openiddict_applications",
                newName: "application_type");

            migrationBuilder.RenameIndex(
                name: "IX_OpenIddictApplications_ClientId",
                schema: "auth",
                table: "openiddict_applications",
                newName: "IX_openiddict_applications_client_id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_openiddict_tokens",
                schema: "auth",
                table: "openiddict_tokens",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_openiddict_scopes",
                schema: "auth",
                table: "openiddict_scopes",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_openiddict_authorizations",
                schema: "auth",
                table: "openiddict_authorizations",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_openiddict_applications",
                schema: "auth",
                table: "openiddict_applications",
                column: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_openiddict_authorizations_openiddict_applications_applicati~",
                schema: "auth",
                table: "openiddict_authorizations",
                column: "application_id",
                principalSchema: "auth",
                principalTable: "openiddict_applications",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_openiddict_tokens_openiddict_applications_application_id",
                schema: "auth",
                table: "openiddict_tokens",
                column: "application_id",
                principalSchema: "auth",
                principalTable: "openiddict_applications",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_openiddict_tokens_openiddict_authorizations_authorization_id",
                schema: "auth",
                table: "openiddict_tokens",
                column: "authorization_id",
                principalSchema: "auth",
                principalTable: "openiddict_authorizations",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_role_claims_roles_role_id",
                schema: "auth",
                table: "role_claims",
                column: "role_id",
                principalSchema: "auth",
                principalTable: "roles",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_user_claims_users_user_id",
                schema: "auth",
                table: "user_claims",
                column: "user_id",
                principalSchema: "auth",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_user_logins_users_user_id",
                schema: "auth",
                table: "user_logins",
                column: "user_id",
                principalSchema: "auth",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_user_roles_roles_role_id",
                schema: "auth",
                table: "user_roles",
                column: "role_id",
                principalSchema: "auth",
                principalTable: "roles",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_user_roles_users_user_id",
                schema: "auth",
                table: "user_roles",
                column: "user_id",
                principalSchema: "auth",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_user_tokens_users_user_id",
                schema: "auth",
                table: "user_tokens",
                column: "user_id",
                principalSchema: "auth",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_openiddict_authorizations_openiddict_applications_applicati~",
                schema: "auth",
                table: "openiddict_authorizations");

            migrationBuilder.DropForeignKey(
                name: "FK_openiddict_tokens_openiddict_applications_application_id",
                schema: "auth",
                table: "openiddict_tokens");

            migrationBuilder.DropForeignKey(
                name: "FK_openiddict_tokens_openiddict_authorizations_authorization_id",
                schema: "auth",
                table: "openiddict_tokens");

            migrationBuilder.DropForeignKey(
                name: "FK_role_claims_roles_role_id",
                schema: "auth",
                table: "role_claims");

            migrationBuilder.DropForeignKey(
                name: "FK_user_claims_users_user_id",
                schema: "auth",
                table: "user_claims");

            migrationBuilder.DropForeignKey(
                name: "FK_user_logins_users_user_id",
                schema: "auth",
                table: "user_logins");

            migrationBuilder.DropForeignKey(
                name: "FK_user_roles_roles_role_id",
                schema: "auth",
                table: "user_roles");

            migrationBuilder.DropForeignKey(
                name: "FK_user_roles_users_user_id",
                schema: "auth",
                table: "user_roles");

            migrationBuilder.DropForeignKey(
                name: "FK_user_tokens_users_user_id",
                schema: "auth",
                table: "user_tokens");

            migrationBuilder.DropPrimaryKey(
                name: "PK_openiddict_tokens",
                schema: "auth",
                table: "openiddict_tokens");

            migrationBuilder.DropPrimaryKey(
                name: "PK_openiddict_scopes",
                schema: "auth",
                table: "openiddict_scopes");

            migrationBuilder.DropPrimaryKey(
                name: "PK_openiddict_authorizations",
                schema: "auth",
                table: "openiddict_authorizations");

            migrationBuilder.DropPrimaryKey(
                name: "PK_openiddict_applications",
                schema: "auth",
                table: "openiddict_applications");

            migrationBuilder.RenameTable(
                name: "openiddict_tokens",
                schema: "auth",
                newName: "OpenIddictTokens",
                newSchema: "auth");

            migrationBuilder.RenameTable(
                name: "openiddict_scopes",
                schema: "auth",
                newName: "OpenIddictScopes",
                newSchema: "auth");

            migrationBuilder.RenameTable(
                name: "openiddict_authorizations",
                schema: "auth",
                newName: "OpenIddictAuthorizations",
                newSchema: "auth");

            migrationBuilder.RenameTable(
                name: "openiddict_applications",
                schema: "auth",
                newName: "OpenIddictApplications",
                newSchema: "auth");

            migrationBuilder.RenameColumn(
                name: "email",
                schema: "auth",
                table: "users",
                newName: "Email");

            migrationBuilder.RenameColumn(
                name: "id",
                schema: "auth",
                table: "users",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "user_name",
                schema: "auth",
                table: "users",
                newName: "UserName");

            migrationBuilder.RenameColumn(
                name: "two_factor_enabled",
                schema: "auth",
                table: "users",
                newName: "TwoFactorEnabled");

            migrationBuilder.RenameColumn(
                name: "security_stamp",
                schema: "auth",
                table: "users",
                newName: "SecurityStamp");

            migrationBuilder.RenameColumn(
                name: "phone_number_confirmed",
                schema: "auth",
                table: "users",
                newName: "PhoneNumberConfirmed");

            migrationBuilder.RenameColumn(
                name: "phone_number",
                schema: "auth",
                table: "users",
                newName: "PhoneNumber");

            migrationBuilder.RenameColumn(
                name: "password_hash",
                schema: "auth",
                table: "users",
                newName: "PasswordHash");

            migrationBuilder.RenameColumn(
                name: "normalized_user_name",
                schema: "auth",
                table: "users",
                newName: "NormalizedUserName");

            migrationBuilder.RenameColumn(
                name: "normalized_email",
                schema: "auth",
                table: "users",
                newName: "NormalizedEmail");

            migrationBuilder.RenameColumn(
                name: "lockout_end",
                schema: "auth",
                table: "users",
                newName: "LockoutEnd");

            migrationBuilder.RenameColumn(
                name: "lockout_enabled",
                schema: "auth",
                table: "users",
                newName: "LockoutEnabled");

            migrationBuilder.RenameColumn(
                name: "email_confirmed",
                schema: "auth",
                table: "users",
                newName: "EmailConfirmed");

            migrationBuilder.RenameColumn(
                name: "display_name",
                schema: "auth",
                table: "users",
                newName: "DisplayName");

            migrationBuilder.RenameColumn(
                name: "concurrency_stamp",
                schema: "auth",
                table: "users",
                newName: "ConcurrencyStamp");

            migrationBuilder.RenameColumn(
                name: "access_failed_count",
                schema: "auth",
                table: "users",
                newName: "AccessFailedCount");

            migrationBuilder.RenameColumn(
                name: "value",
                schema: "auth",
                table: "user_tokens",
                newName: "Value");

            migrationBuilder.RenameColumn(
                name: "name",
                schema: "auth",
                table: "user_tokens",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "login_provider",
                schema: "auth",
                table: "user_tokens",
                newName: "LoginProvider");

            migrationBuilder.RenameColumn(
                name: "user_id",
                schema: "auth",
                table: "user_tokens",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "role_id",
                schema: "auth",
                table: "user_roles",
                newName: "RoleId");

            migrationBuilder.RenameColumn(
                name: "user_id",
                schema: "auth",
                table: "user_roles",
                newName: "UserId");

            migrationBuilder.RenameIndex(
                name: "IX_user_roles_role_id",
                schema: "auth",
                table: "user_roles",
                newName: "IX_user_roles_RoleId");

            migrationBuilder.RenameColumn(
                name: "user_id",
                schema: "auth",
                table: "user_logins",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "provider_display_name",
                schema: "auth",
                table: "user_logins",
                newName: "ProviderDisplayName");

            migrationBuilder.RenameColumn(
                name: "provider_key",
                schema: "auth",
                table: "user_logins",
                newName: "ProviderKey");

            migrationBuilder.RenameColumn(
                name: "login_provider",
                schema: "auth",
                table: "user_logins",
                newName: "LoginProvider");

            migrationBuilder.RenameIndex(
                name: "IX_user_logins_user_id",
                schema: "auth",
                table: "user_logins",
                newName: "IX_user_logins_UserId");

            migrationBuilder.RenameColumn(
                name: "id",
                schema: "auth",
                table: "user_claims",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "user_id",
                schema: "auth",
                table: "user_claims",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "claim_value",
                schema: "auth",
                table: "user_claims",
                newName: "ClaimValue");

            migrationBuilder.RenameColumn(
                name: "claim_type",
                schema: "auth",
                table: "user_claims",
                newName: "ClaimType");

            migrationBuilder.RenameIndex(
                name: "IX_user_claims_user_id",
                schema: "auth",
                table: "user_claims",
                newName: "IX_user_claims_UserId");

            migrationBuilder.RenameColumn(
                name: "name",
                schema: "auth",
                table: "roles",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "id",
                schema: "auth",
                table: "roles",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "normalized_name",
                schema: "auth",
                table: "roles",
                newName: "NormalizedName");

            migrationBuilder.RenameColumn(
                name: "concurrency_stamp",
                schema: "auth",
                table: "roles",
                newName: "ConcurrencyStamp");

            migrationBuilder.RenameColumn(
                name: "id",
                schema: "auth",
                table: "role_claims",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "role_id",
                schema: "auth",
                table: "role_claims",
                newName: "RoleId");

            migrationBuilder.RenameColumn(
                name: "claim_value",
                schema: "auth",
                table: "role_claims",
                newName: "ClaimValue");

            migrationBuilder.RenameColumn(
                name: "claim_type",
                schema: "auth",
                table: "role_claims",
                newName: "ClaimType");

            migrationBuilder.RenameIndex(
                name: "IX_role_claims_role_id",
                schema: "auth",
                table: "role_claims",
                newName: "IX_role_claims_RoleId");

            migrationBuilder.RenameColumn(
                name: "type",
                schema: "auth",
                table: "OpenIddictTokens",
                newName: "Type");

            migrationBuilder.RenameColumn(
                name: "subject",
                schema: "auth",
                table: "OpenIddictTokens",
                newName: "Subject");

            migrationBuilder.RenameColumn(
                name: "status",
                schema: "auth",
                table: "OpenIddictTokens",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "properties",
                schema: "auth",
                table: "OpenIddictTokens",
                newName: "Properties");

            migrationBuilder.RenameColumn(
                name: "payload",
                schema: "auth",
                table: "OpenIddictTokens",
                newName: "Payload");

            migrationBuilder.RenameColumn(
                name: "id",
                schema: "auth",
                table: "OpenIddictTokens",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "reference_id",
                schema: "auth",
                table: "OpenIddictTokens",
                newName: "ReferenceId");

            migrationBuilder.RenameColumn(
                name: "redemption_date",
                schema: "auth",
                table: "OpenIddictTokens",
                newName: "RedemptionDate");

            migrationBuilder.RenameColumn(
                name: "expiration_date",
                schema: "auth",
                table: "OpenIddictTokens",
                newName: "ExpirationDate");

            migrationBuilder.RenameColumn(
                name: "creation_date",
                schema: "auth",
                table: "OpenIddictTokens",
                newName: "CreationDate");

            migrationBuilder.RenameColumn(
                name: "concurrency_token",
                schema: "auth",
                table: "OpenIddictTokens",
                newName: "ConcurrencyToken");

            migrationBuilder.RenameColumn(
                name: "authorization_id",
                schema: "auth",
                table: "OpenIddictTokens",
                newName: "AuthorizationId");

            migrationBuilder.RenameColumn(
                name: "application_id",
                schema: "auth",
                table: "OpenIddictTokens",
                newName: "ApplicationId");

            migrationBuilder.RenameIndex(
                name: "IX_openiddict_tokens_reference_id",
                schema: "auth",
                table: "OpenIddictTokens",
                newName: "IX_OpenIddictTokens_ReferenceId");

            migrationBuilder.RenameIndex(
                name: "IX_openiddict_tokens_authorization_id",
                schema: "auth",
                table: "OpenIddictTokens",
                newName: "IX_OpenIddictTokens_AuthorizationId");

            migrationBuilder.RenameIndex(
                name: "IX_openiddict_tokens_application_id_status_subject_type",
                schema: "auth",
                table: "OpenIddictTokens",
                newName: "IX_OpenIddictTokens_ApplicationId_Status_Subject_Type");

            migrationBuilder.RenameColumn(
                name: "resources",
                schema: "auth",
                table: "OpenIddictScopes",
                newName: "Resources");

            migrationBuilder.RenameColumn(
                name: "properties",
                schema: "auth",
                table: "OpenIddictScopes",
                newName: "Properties");

            migrationBuilder.RenameColumn(
                name: "name",
                schema: "auth",
                table: "OpenIddictScopes",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "descriptions",
                schema: "auth",
                table: "OpenIddictScopes",
                newName: "Descriptions");

            migrationBuilder.RenameColumn(
                name: "description",
                schema: "auth",
                table: "OpenIddictScopes",
                newName: "Description");

            migrationBuilder.RenameColumn(
                name: "id",
                schema: "auth",
                table: "OpenIddictScopes",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "display_names",
                schema: "auth",
                table: "OpenIddictScopes",
                newName: "DisplayNames");

            migrationBuilder.RenameColumn(
                name: "display_name",
                schema: "auth",
                table: "OpenIddictScopes",
                newName: "DisplayName");

            migrationBuilder.RenameColumn(
                name: "concurrency_token",
                schema: "auth",
                table: "OpenIddictScopes",
                newName: "ConcurrencyToken");

            migrationBuilder.RenameIndex(
                name: "IX_openiddict_scopes_name",
                schema: "auth",
                table: "OpenIddictScopes",
                newName: "IX_OpenIddictScopes_Name");

            migrationBuilder.RenameColumn(
                name: "type",
                schema: "auth",
                table: "OpenIddictAuthorizations",
                newName: "Type");

            migrationBuilder.RenameColumn(
                name: "subject",
                schema: "auth",
                table: "OpenIddictAuthorizations",
                newName: "Subject");

            migrationBuilder.RenameColumn(
                name: "status",
                schema: "auth",
                table: "OpenIddictAuthorizations",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "scopes",
                schema: "auth",
                table: "OpenIddictAuthorizations",
                newName: "Scopes");

            migrationBuilder.RenameColumn(
                name: "properties",
                schema: "auth",
                table: "OpenIddictAuthorizations",
                newName: "Properties");

            migrationBuilder.RenameColumn(
                name: "id",
                schema: "auth",
                table: "OpenIddictAuthorizations",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "creation_date",
                schema: "auth",
                table: "OpenIddictAuthorizations",
                newName: "CreationDate");

            migrationBuilder.RenameColumn(
                name: "concurrency_token",
                schema: "auth",
                table: "OpenIddictAuthorizations",
                newName: "ConcurrencyToken");

            migrationBuilder.RenameColumn(
                name: "application_id",
                schema: "auth",
                table: "OpenIddictAuthorizations",
                newName: "ApplicationId");

            migrationBuilder.RenameIndex(
                name: "IX_openiddict_authorizations_application_id_status_subject_type",
                schema: "auth",
                table: "OpenIddictAuthorizations",
                newName: "IX_OpenIddictAuthorizations_ApplicationId_Status_Subject_Type");

            migrationBuilder.RenameColumn(
                name: "settings",
                schema: "auth",
                table: "OpenIddictApplications",
                newName: "Settings");

            migrationBuilder.RenameColumn(
                name: "requirements",
                schema: "auth",
                table: "OpenIddictApplications",
                newName: "Requirements");

            migrationBuilder.RenameColumn(
                name: "properties",
                schema: "auth",
                table: "OpenIddictApplications",
                newName: "Properties");

            migrationBuilder.RenameColumn(
                name: "permissions",
                schema: "auth",
                table: "OpenIddictApplications",
                newName: "Permissions");

            migrationBuilder.RenameColumn(
                name: "id",
                schema: "auth",
                table: "OpenIddictApplications",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "redirect_uris",
                schema: "auth",
                table: "OpenIddictApplications",
                newName: "RedirectUris");

            migrationBuilder.RenameColumn(
                name: "post_logout_redirect_uris",
                schema: "auth",
                table: "OpenIddictApplications",
                newName: "PostLogoutRedirectUris");

            migrationBuilder.RenameColumn(
                name: "json_web_key_set",
                schema: "auth",
                table: "OpenIddictApplications",
                newName: "JsonWebKeySet");

            migrationBuilder.RenameColumn(
                name: "display_names",
                schema: "auth",
                table: "OpenIddictApplications",
                newName: "DisplayNames");

            migrationBuilder.RenameColumn(
                name: "display_name",
                schema: "auth",
                table: "OpenIddictApplications",
                newName: "DisplayName");

            migrationBuilder.RenameColumn(
                name: "consent_type",
                schema: "auth",
                table: "OpenIddictApplications",
                newName: "ConsentType");

            migrationBuilder.RenameColumn(
                name: "concurrency_token",
                schema: "auth",
                table: "OpenIddictApplications",
                newName: "ConcurrencyToken");

            migrationBuilder.RenameColumn(
                name: "client_type",
                schema: "auth",
                table: "OpenIddictApplications",
                newName: "ClientType");

            migrationBuilder.RenameColumn(
                name: "client_secret",
                schema: "auth",
                table: "OpenIddictApplications",
                newName: "ClientSecret");

            migrationBuilder.RenameColumn(
                name: "client_id",
                schema: "auth",
                table: "OpenIddictApplications",
                newName: "ClientId");

            migrationBuilder.RenameColumn(
                name: "application_type",
                schema: "auth",
                table: "OpenIddictApplications",
                newName: "ApplicationType");

            migrationBuilder.RenameIndex(
                name: "IX_openiddict_applications_client_id",
                schema: "auth",
                table: "OpenIddictApplications",
                newName: "IX_OpenIddictApplications_ClientId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_OpenIddictTokens",
                schema: "auth",
                table: "OpenIddictTokens",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_OpenIddictScopes",
                schema: "auth",
                table: "OpenIddictScopes",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_OpenIddictAuthorizations",
                schema: "auth",
                table: "OpenIddictAuthorizations",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_OpenIddictApplications",
                schema: "auth",
                table: "OpenIddictApplications",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_OpenIddictAuthorizations_OpenIddictApplications_Application~",
                schema: "auth",
                table: "OpenIddictAuthorizations",
                column: "ApplicationId",
                principalSchema: "auth",
                principalTable: "OpenIddictApplications",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_OpenIddictTokens_OpenIddictApplications_ApplicationId",
                schema: "auth",
                table: "OpenIddictTokens",
                column: "ApplicationId",
                principalSchema: "auth",
                principalTable: "OpenIddictApplications",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_OpenIddictTokens_OpenIddictAuthorizations_AuthorizationId",
                schema: "auth",
                table: "OpenIddictTokens",
                column: "AuthorizationId",
                principalSchema: "auth",
                principalTable: "OpenIddictAuthorizations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_role_claims_roles_RoleId",
                schema: "auth",
                table: "role_claims",
                column: "RoleId",
                principalSchema: "auth",
                principalTable: "roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_user_claims_users_UserId",
                schema: "auth",
                table: "user_claims",
                column: "UserId",
                principalSchema: "auth",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_user_logins_users_UserId",
                schema: "auth",
                table: "user_logins",
                column: "UserId",
                principalSchema: "auth",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_user_roles_roles_RoleId",
                schema: "auth",
                table: "user_roles",
                column: "RoleId",
                principalSchema: "auth",
                principalTable: "roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_user_roles_users_UserId",
                schema: "auth",
                table: "user_roles",
                column: "UserId",
                principalSchema: "auth",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_user_tokens_users_UserId",
                schema: "auth",
                table: "user_tokens",
                column: "UserId",
                principalSchema: "auth",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
