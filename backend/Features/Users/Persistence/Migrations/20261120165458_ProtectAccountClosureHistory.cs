using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TemplateApp.Features.Users.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProtectAccountClosureHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE FUNCTION users.reject_account_closure_history_change() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'users."AccountClosureHistory" is append-only: % is not allowed', TG_OP
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;

                CREATE TRIGGER "AccountClosureHistory_AppendOnly"
                BEFORE UPDATE OR DELETE ON users."AccountClosureHistory"
                FOR EACH ROW EXECUTE FUNCTION users.reject_account_closure_history_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER "AccountClosureHistory_AppendOnly" ON users."AccountClosureHistory";
                DROP FUNCTION users.reject_account_closure_history_change();
                """);
        }
    }
}
