-- Edit the email and role below, then run this file with psql.
-- Valid roles: Employee, InnovateManager, InnovateBoard.
WITH requested AS (
    SELECT LOWER('manager@example.com') AS email,
           'InnovateManager' AS role_name
), matched AS (
    SELECT u."Id" AS user_id, r."Id" AS role_id
    FROM requested q
    JOIN "AspNetUsers" u ON LOWER(u."Email") = q.email
    JOIN "AspNetRoles" r ON r."NormalizedName" = UPPER(q.role_name)
)
INSERT INTO "AspNetUserRoles" ("UserId", "RoleId")
SELECT user_id, role_id FROM matched
ON CONFLICT ("UserId", "RoleId") DO NOTHING;

-- Confirm the membership. No row means the email/role wasn't found or assigned.
SELECT u."Email", r."Name" AS "Role"
FROM "AspNetUserRoles" ur
JOIN "AspNetUsers" u ON u."Id" = ur."UserId"
JOIN "AspNetRoles" r ON r."Id" = ur."RoleId"
WHERE LOWER(u."Email") = LOWER('manager@example.com')
  AND r."NormalizedName" = UPPER('InnovateManager');
