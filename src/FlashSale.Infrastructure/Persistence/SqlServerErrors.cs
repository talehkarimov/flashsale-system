using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FlashSale.Infrastructure.Persistence;

internal static class SqlServerErrors
{
    private const int DuplicateIndexKey = 2601;
    private const int UniqueConstraintViolation = 2627;

    public static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException sqlException &&
        sqlException.Errors.Cast<SqlError>().Any(error => error.Number is DuplicateIndexKey or UniqueConstraintViolation);
}
