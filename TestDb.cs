using Npgsql;

class TestDb
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Testing Database Connection ===\n");

        // Спробуйте різні варіанти connection string
        var connectionStrings = new Dictionary<string, string>
        {
            ["Direct"] = "Host=db.mnzgzwlvlqfnvmvntzzo.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=4545Eb11092025!;SSL Mode=Require",

            ["Pooler"] = "Host=aws-0-eu-central-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.mnzgzwlvlqfnvmvntzzo;Password=4545Eb11092025!;SSL Mode=Require",

            ["NoSSL"] = "Host=db.mnzgzwlvlqfnvmvntzzo.supabase.co;Port=5432;Database=postgres;Username=postgres;Password=4545Eb11092025!"
        };

        foreach (var kvp in connectionStrings)
        {
            Console.WriteLine($"Testing {kvp.Key} connection...");

            try
            {
                using var conn = new NpgsqlConnection(kvp.Value);
                await conn.OpenAsync();

                Console.WriteLine($"✓ {kvp.Key}: Connected successfully!");

                // Спробуємо виконати простий запит
                using var cmd = new NpgsqlCommand("SELECT version()", conn);
                var version = await cmd.ExecuteScalarAsync();
                Console.WriteLine($"  PostgreSQL version: {version}");

                // Перевіримо таблицю Examiners
                using var cmd2 = new NpgsqlCommand("SELECT COUNT(*) FROM \"Examiners\"", conn);
                var count = await cmd2.ExecuteScalarAsync();
                Console.WriteLine($"  Examiners count: {count}");

                conn.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ {kvp.Key}: Failed");
                Console.WriteLine($"  Error: {ex.Message}\n");
            }
        }

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }
}