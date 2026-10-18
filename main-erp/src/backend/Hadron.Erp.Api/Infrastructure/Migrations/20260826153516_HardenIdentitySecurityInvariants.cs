using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hadron.Erp.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HardenIdentitySecurityInvariants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION identity.enforce_last_system_administrator()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    affected_user_id uuid;
                    removing_system_role boolean := false;
                    remaining_active_administrators integer;
                BEGIN
                    PERFORM pg_advisory_xact_lock(1212236882, 1330528580);

                    IF TG_TABLE_NAME = 'users' THEN
                        IF TG_OP = 'UPDATE' AND OLD."IsActive" AND NOT NEW."IsActive" THEN
                            affected_user_id := OLD."Id";
                        ELSIF TG_OP = 'DELETE' AND OLD."IsActive" THEN
                            affected_user_id := OLD."Id";
                        ELSE
                            IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                            RETURN NEW;
                        END IF;
                    ELSIF TG_TABLE_NAME = 'user_roles' THEN
                        IF TG_OP = 'DELETE' OR
                           (TG_OP = 'UPDATE' AND (OLD."UserId" <> NEW."UserId" OR OLD."RoleId" <> NEW."RoleId")) THEN
                            affected_user_id := OLD."UserId";
                            SELECT r."IsSystemRole"
                            INTO removing_system_role
                            FROM identity.roles AS r
                            WHERE r."Id" = OLD."RoleId";
                            IF NOT COALESCE(removing_system_role, false) THEN
                                IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                                RETURN NEW;
                            END IF;
                        ELSE
                            IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                            RETURN NEW;
                        END IF;
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1
                        FROM identity.user_roles AS ur
                        JOIN identity.roles AS r ON r."Id" = ur."RoleId" AND r."IsSystemRole"
                        JOIN identity.users AS u ON u."Id" = ur."UserId" AND u."IsActive"
                        WHERE ur."UserId" = affected_user_id)
                    THEN
                        IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                        RETURN NEW;
                    END IF;

                    SELECT COUNT(*)
                    INTO remaining_active_administrators
                    FROM identity.user_roles AS ur
                    JOIN identity.roles AS r ON r."Id" = ur."RoleId" AND r."IsSystemRole"
                    JOIN identity.users AS u ON u."Id" = ur."UserId" AND u."IsActive"
                    WHERE ur."UserId" <> affected_user_id;

                    IF remaining_active_administrators = 0 THEN
                        RAISE EXCEPTION USING
                            ERRCODE = '23514',
                            MESSAGE = 'LAST_SYSTEM_ADMINISTRATOR';
                    END IF;
                    IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                    RETURN NEW;
                END;
                $function$;

                CREATE TRIGGER trg_identity_users_last_system_administrator
                BEFORE UPDATE OF "IsActive" OR DELETE ON identity.users
                FOR EACH ROW EXECUTE FUNCTION identity.enforce_last_system_administrator();

                CREATE TRIGGER trg_identity_user_roles_last_system_administrator
                BEFORE UPDATE OR DELETE ON identity.user_roles
                FOR EACH ROW EXECUTE FUNCTION identity.enforce_last_system_administrator();

                CREATE OR REPLACE FUNCTION identity.protect_system_role()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    IF OLD."IsSystemRole" THEN
                        IF TG_OP = 'DELETE' OR NEW."IsSystemRole" IS DISTINCT FROM OLD."IsSystemRole" OR
                           NEW."Name" IS DISTINCT FROM OLD."Name" THEN
                            RAISE EXCEPTION USING ERRCODE = '23514', MESSAGE = 'SYSTEM_ROLE_IMMUTABLE';
                        END IF;
                    END IF;
                    IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                    RETURN NEW;
                END;
                $function$;

                CREATE TRIGGER trg_identity_roles_protect_system_role
                BEFORE UPDATE OR DELETE ON identity.roles
                FOR EACH ROW EXECUTE FUNCTION identity.protect_system_role();

                CREATE OR REPLACE FUNCTION identity.prevent_security_audit_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    RAISE EXCEPTION USING ERRCODE = '23514', MESSAGE = 'SECURITY_AUDIT_IMMUTABLE';
                END;
                $function$;

                CREATE TRIGGER trg_identity_security_audit_immutable
                BEFORE UPDATE OR DELETE ON identity.security_audit_events
                FOR EACH ROW EXECUTE FUNCTION identity.prevent_security_audit_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_identity_security_audit_immutable ON identity.security_audit_events;
                DROP FUNCTION IF EXISTS identity.prevent_security_audit_mutation();
                DROP TRIGGER IF EXISTS trg_identity_roles_protect_system_role ON identity.roles;
                DROP FUNCTION IF EXISTS identity.protect_system_role();
                DROP TRIGGER IF EXISTS trg_identity_user_roles_last_system_administrator ON identity.user_roles;
                DROP TRIGGER IF EXISTS trg_identity_users_last_system_administrator ON identity.users;
                DROP FUNCTION IF EXISTS identity.enforce_last_system_administrator();
                """);
        }
    }
}
