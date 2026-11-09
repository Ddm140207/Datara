namespace Datara.Application.Common;

/// <summary>
/// Mirrors the Python analysis service contract. Kept in one file so the two
/// schemas can be diffed at a glance.
/// </summary>
public static class ApiRoutes
{
    public const string Base = "api/v1";

    public static class Datasets
    {
        public const string Root = Base + "/datasets";
        public const string ById = Base + "/datasets/{id:guid}";
        public const string Overview = Base + "/datasets/{id:guid}/overview";
        public const string Columns = Base + "/datasets/{id:guid}/columns";
        public const string Column = Base + "/datasets/{id:guid}/columns/{column}";
        public const string Issues = Base + "/datasets/{id:guid}/issues";
        public const string Correlations = Base + "/datasets/{id:guid}/correlations";
        public const string Samples = Base + "/datasets/{id:guid}/samples";
        public const string Analyse = Base + "/datasets/{id:guid}/analyse";
    }

    public static class Ai
    {
        public const string Summary = Base + "/datasets/{id:guid}/ai/summary";
        public const string Chat = Base + "/datasets/{id:guid}/ai/chat";
        public const string Capabilities = Base + "/ai/capabilities";
    }

    public static class System
    {
        public const string Health = Base + "/health";
    }
}