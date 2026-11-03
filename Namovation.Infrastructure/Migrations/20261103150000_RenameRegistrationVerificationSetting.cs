using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Namovation.Infrastructure.Data;

namespace Namovation.Infrastructure.Migrations;

[DbContext(typeof(NamovationDbContext))]
[Migration("20261103150000_RenameRegistrationVerificationSetting")]
public sealed class RenameRegistrationVerificationSetting : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE "RegistrationSettings"
        RENAME COLUMN "EmailVerificationEnabled" TO "AuthenticatorSetupRequired";
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE "RegistrationSettings"
        RENAME COLUMN "AuthenticatorSetupRequired" TO "EmailVerificationEnabled";
        """);
}
