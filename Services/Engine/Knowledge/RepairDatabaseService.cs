using System;
using System.IO;
using LiteDB;

namespace Syncro.Desktop.Services.Engine.Knowledge
{
    public class RepairIndexModel
    {
        public ObjectId Id { get; set; }
        public string Framework { get; set; }
        public string ErrorCode { get; set; }
        public string RepairDir { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool HasPatch { get; set; }
        public bool HasScript { get; set; }
    }

    public class RepairDatabaseService
    {
        // This method opens a connection to the specific workspace's LiteDB
        // It returns the connection so the tool can query it and dispose it.
        public LiteDatabase OpenWorkspaceDb(string workspacePath)
        {
            string dbPath = Path.Combine(workspacePath, ".syncro", "syncro.db");
            var dir = Path.GetDirectoryName(dbPath);
            if (dir != null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var db = new LiteDatabase(dbPath);
            var repairs = db.GetCollection<RepairIndexModel>("Repairs");
            
            // Ensure fast querying
            repairs.EnsureIndex(x => x.Framework);
            repairs.EnsureIndex(x => x.ErrorCode);

            return db;
        }
    }
}
