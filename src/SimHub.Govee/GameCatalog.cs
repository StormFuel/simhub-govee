using SimHub.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SimHub.Govee
{
    public sealed class GameCatalogItem
    {
        public string Code { get; set; }
        public string Name { get; set; }
        public bool Hidden { get; set; }
        public string DisplayName => string.IsNullOrWhiteSpace(Name) || string.Equals(Name, Code, StringComparison.OrdinalIgnoreCase) ? Code : Name + " (" + Code + ")";
    }

    public static class SimHubGameCatalog
    {
        public static IList<GameCatalogItem> GetGames(IEnumerable<string> alwaysInclude = null)
        {
            var include = new HashSet<string>(alwaysInclude ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            try
            {
                var result = Configuration.Games
                    .Where(g => g != null && !string.IsNullOrWhiteSpace(g.Code))
                    .Select(g => new GameCatalogItem { Code = g.Code, Name = g.Name, Hidden = g.IsHidden })
                    .Where(g => !g.Hidden || include.Contains(g.Code))
                    .GroupBy(g => g.Code, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .OrderBy(g => g.Name ?? g.Code, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                foreach (string code in include.Where(x => !string.IsNullOrWhiteSpace(x) && !result.Any(g => string.Equals(g.Code, x, StringComparison.OrdinalIgnoreCase)))) result.Add(new GameCatalogItem { Code = code, Name = code });
                return result;
            }
            catch
            {
                return include.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Select(x => new GameCatalogItem { Code = x, Name = x }).OrderBy(x => x.Name).ToList();
            }
        }

        public static IList<GameCatalogItem> Filter(IEnumerable<GameCatalogItem> games, IEnumerable<string> alwaysInclude = null)
        {
            var include = new HashSet<string>(alwaysInclude ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            return (games ?? Enumerable.Empty<GameCatalogItem>()).Where(g => g != null && !string.IsNullOrWhiteSpace(g.Code) && (!g.Hidden || include.Contains(g.Code))).GroupBy(g => g.Code, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).OrderBy(g => g.Name ?? g.Code, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
    }
}
