namespace TimeSheet.BLL.Extensions
{
    // Builds a "contains" pattern for EF.Functions.ILike with LIKE wildcards in the user's text escaped.
    public static class SearchPattern
    {
        public const string Escape = @"\";   // Npgsql uses ESCAPE '' unless you pass one

        public static string Contains(string search)
        {
            var e = search
                .Replace(Escape, Escape + Escape)
                .Replace("%", Escape + "%")
                .Replace("_", Escape + "_");
            return $"%{e}%";
        }
    }
}
