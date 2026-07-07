namespace Syncro.Desktop.Services.Connector.Models
{
    /// <summary>
    /// One field in a request or response shape. Kept deliberately shallow (top-level only)
    /// so it can be compared cheaply between a frontend call and a backend route.
    /// </summary>
    public sealed class ApiField
    {
        public string Name { get; set; } = "";

        /// <summary>OpenAPI/JSON type hint: string, integer, number, boolean, object, array.</summary>
        public string Type { get; set; } = "any";

        public bool Required { get; set; }
    }
}
