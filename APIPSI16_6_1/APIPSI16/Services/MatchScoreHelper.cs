namespace APIPSI16.Services
{
    /// <summary>
    /// Shared helpers for calculating opportunity-to-user match scores.
    /// Match is based on job role preferences (70% weight) and location (30% weight).
    /// </summary>
    public static class MatchScoreHelper
    {
        public const double RoleWeight = 0.7;
        public const double LocationWeight = 0.3;

        /// <summary>
        /// Returns true when the user's location and the opportunity's location are considered
        /// a match (case-insensitive substring check in both directions).
        /// </summary>
        public static bool LocationsMatch(string? userLocation, string? opportunityLocation)
        {
            if (string.IsNullOrWhiteSpace(userLocation) || string.IsNullOrWhiteSpace(opportunityLocation))
                return false;

            var ul = userLocation.Trim();
            var ol = opportunityLocation.Trim();
            return ol.Contains(ul, StringComparison.OrdinalIgnoreCase)
                || ul.Contains(ol, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Combines a role score (0-100) and a location match flag into a single weighted percentage.
        /// </summary>
        /// <param name="roleScore">0-100 score based on job-role overlap.</param>
        /// <param name="locationMatched">Whether the user's location matches the opportunity's location.</param>
        /// <param name="hasRoles">Whether the opportunity specifies any required job roles.</param>
        /// <param name="hasLocations">Whether both the user and the opportunity have location data.</param>
        /// <returns>Weighted match percentage (0-100).</returns>
        public static int ComputeWeightedScore(int roleScore, bool locationMatched, bool hasRoles, bool hasLocations)
        {
            int locationScore = locationMatched ? 100 : 0;

            if (hasRoles && hasLocations)
                return (int)Math.Round(roleScore * RoleWeight + locationScore * LocationWeight);
            if (hasRoles)
                return roleScore;
            if (hasLocations)
                return locationScore;
            return 0;
        }
    }
}
