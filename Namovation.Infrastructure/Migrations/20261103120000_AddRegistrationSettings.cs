using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Namovation.Infrastructure.Data;

namespace Namovation.Infrastructure.Migrations;

[DbContext(typeof(NamovationDbContext))]
[Migration("20261103120000_AddRegistrationSettings")]
public sealed class AddRegistrationSettings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE TABLE "RegistrationSettings" (
            "Id" integer NOT NULL,
            "EmailVerificationEnabled" boolean NOT NULL DEFAULT TRUE,
            "UpdatedAtUtc" timestamp with time zone NOT NULL,
            "UpdatedByUserId" text NULL,
            CONSTRAINT "PK_RegistrationSettings" PRIMARY KEY ("Id"),
            CONSTRAINT "CK_RegistrationSettings_SingletonId" CHECK ("Id" = 1));
        INSERT INTO "RegistrationSettings" ("Id", "EmailVerificationEnabled", "UpdatedAtUtc", "UpdatedByUserId")
        VALUES (1, TRUE, CURRENT_TIMESTAMP, NULL);
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("DROP TABLE IF EXISTS \"RegistrationSettings\";");
}
