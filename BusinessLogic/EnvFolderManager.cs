using System;
using System.IO;
using System.Collections.Generic;

namespace DeveloperAI.BusinessLogic
{
    public static class EnvFolderManager
    {
        public static readonly string[] Languages = { "python", "node", "java", "cpp", "rust", "go" };

        public static string GetEnvRoot(string projectRoot)
        {
            return Path.Combine(projectRoot, "env");
        }

        public static void EnsureEnvFolders(string projectRoot)
        {
            string envRoot = GetEnvRoot(projectRoot);
            Directory.CreateDirectory(envRoot);
            foreach (var lang in Languages)
            {
                Directory.CreateDirectory(Path.Combine(envRoot, lang));
            }
        }

        public static string GetEnvFolder(string projectRoot, string language)
        {
            return Path.Combine(GetEnvRoot(projectRoot), language.ToLower());
        }

        // Returns a dictionary of language -> full path
        public static Dictionary<string, string> GetAllEnvFolders(string projectRoot)
        {
            var dict = new Dictionary<string, string>();
            foreach (var lang in Languages)
            {
                dict[lang] = GetEnvFolder(projectRoot, lang);
            }
            return dict;
        }
    }
} 