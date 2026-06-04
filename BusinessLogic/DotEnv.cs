using System;
using System.IO;

namespace DeveloperAI.BusinessLogic
{
    public static class DotEnv
    {
        public static void Load(string filePath)
        {
            if (!File.Exists(filePath))
                return;

            var directory = Path.GetDirectoryName(filePath);

            foreach (var line in File.ReadAllLines(filePath))
            {
                var parts = line.Split('=', 2);

                if (parts.Length != 2)
                    continue;

                var key = parts[0];
                var value = parts[1];

                if (key == "GOOGLE_APPLICATION_CREDENTIALS" && !Path.IsPathRooted(value) && directory != null)
                {
                    value = Path.GetFullPath(Path.Combine(directory, value));
                }

                Environment.SetEnvironmentVariable(key, value);
            }
        }
    }
}
