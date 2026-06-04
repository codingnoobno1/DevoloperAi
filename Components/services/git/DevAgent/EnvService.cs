namespace Syncro.Desktop.Services.DevAgent
{
    public class EnvService
    {
        private readonly string _envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");

        public Dictionary<string, string> LoadEnv()
        {
            var env = new Dictionary<string, string>();
            if (!File.Exists(_envPath)) return env;

            foreach (var line in File.ReadAllLines(_envPath))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                var parts = line.Split('=', 2);
                if (parts.Length == 2)
                    env[parts[0].Trim()] = parts[1].Trim();
            }
            return env;
        }

        public void SaveEnv(Dictionary<string, string> env)
        {
            var lines = env.Select(kv => $"{kv.Key}={kv.Value}");
            File.WriteAllLines(_envPath, lines);
        }

        public string? GetEnvValue(string key) =>
            Environment.GetEnvironmentVariable(key) ??
            LoadEnv().GetValueOrDefault(key);
    }
}
