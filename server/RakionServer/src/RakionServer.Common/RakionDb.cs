namespace RakionServer.Common
{
    /// <summary>
    /// Monta a connection string do MariaDB/MySQL do Rakion (banco `rakion`). FONTE ÚNICA do template:
    /// o World (WorldConfig.DbConfig) e o Buddy (BuddyConfig) usam este mesmo builder para não divergirem
    /// nos parâmetros de pool/timeout/SSL — sem isto cada serviço repetiria a string e ela se desincronizaria.
    /// </summary>
    public static class RakionDb
    {
        public static string BuildConnectionString(string ip, int port, string database, string user, string pass) =>
            $"Server={ip};Port={port};Database={database};Uid={user};Pwd={pass};" +
            "Pooling=true;Default Command Timeout=15;Connection Timeout=10;" +
            "AllowPublicKeyRetrieval=true;SslMode=None";
    }
}
