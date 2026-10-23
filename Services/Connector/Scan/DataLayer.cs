using System.Collections.Generic;

namespace Syncro.Desktop.Services.Connector.Scan
{
    /// <summary>A relational table inferred from an EF <c>DbSet&lt;Entity&gt; Name</c> declaration.</summary>
    public sealed class SqlTable
    {
        public string Name { get; set; } = "";     // the DbSet property name (≈ table)
        public string Entity { get; set; } = "";    // the mapped entity type
    }

    /// <summary>A MongoDB collection inferred from <c>GetCollection&lt;Doc&gt;("name")</c>.</summary>
    public sealed class MongoCollection
    {
        public string Name { get; set; } = "";      // collection name, e.g. "pg_listings"
        public string Document { get; set; } = "";   // the mapped document type
    }

    /// <summary>One field/property of a model or DTO — the actual structure.</summary>
    public sealed class TypeField
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
    }

    /// <summary>A model/entity or DTO with its fields, so the structure is explorable — not just a name.</summary>
    public sealed class TypeDef
    {
        public string Name { get; set; } = "";
        public string File { get; set; } = "";        // relative to scan root
        public int Line { get; set; }
        public List<TypeField> Fields { get; set; } = new();
    }

    /// <summary>The persistence + type surface of one project — models, DTOs and the database schema.</summary>
    public sealed class DataLayer
    {
        public List<string> Models { get; set; } = new();            // entities / domain models (names — back-compat)
        public List<string> Dtos { get; set; } = new();              // request/response/DTO types (names — back-compat)
        public List<TypeDef> ModelTypes { get; set; } = new();       // models WITH fields (explorable)
        public List<TypeDef> DtoTypes { get; set; } = new();         // DTOs WITH fields (explorable)
        public List<SqlTable> Tables { get; set; } = new();          // EF relational tables
        public List<MongoCollection> Collections { get; set; } = new(); // Mongo collections
        public List<string> SqlScripts { get; set; } = new();        // .sql file names
        public int Migrations { get; set; }                          // EF migration count
        public string? Store { get; set; }                           // detected store: Postgres | SqlServer | MongoDB | Hive | mixed

        public int TableCount => Tables.Count + Collections.Count;
        public bool IsEmpty => Models.Count == 0 && Dtos.Count == 0 && TableCount == 0 && SqlScripts.Count == 0;
    }
}
