namespace RakionServer.Common;

public static class LauncherTicketSchema
{
    public const string CreateSql =
        "CREATE TABLE IF NOT EXISTS launcher_ticket (" +
        "token_hash BINARY(32) NOT NULL PRIMARY KEY," +
        "account_id VARCHAR(16) NOT NULL," +
        "app_id INT NULL," +
        "build_version INT NULL," +
        "expires_at DATETIME(6) NOT NULL," +
        "used_at DATETIME(6) NULL," +
        "created_at DATETIME(6) NOT NULL," +
        "INDEX ix_launcher_ticket_account(account_id,expires_at)) ENGINE=InnoDB";

    public static readonly string[] MigrationSql =
    {
        "ALTER TABLE launcher_ticket ADD COLUMN IF NOT EXISTS app_id INT NULL AFTER account_id",
        "ALTER TABLE launcher_ticket ADD COLUMN IF NOT EXISTS build_version INT NULL AFTER app_id"
    };
}
