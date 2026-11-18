using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace TemplateApp.Features.Projects.Persistence;

internal static class ProjectNameUniqueness
{
    public static bool IsViolatedBy(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ProjectConfiguration.UniqueNameIndex,
        };
}
