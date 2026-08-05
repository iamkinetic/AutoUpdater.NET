using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ZipExtractor
{
    /// <summary>
    ///     Decides which file and directory names survive the clear phase, either by exact name or by wildcard pattern.
    /// </summary>
    public class PreservedEntries
    {
        private readonly ISet<string> _names;
        private readonly IList<Regex> _patterns;

        public PreservedEntries(IEnumerable<string> names, IEnumerable<string> patterns)
        {
            _names = new HashSet<string>(names ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            _patterns = (patterns ?? Enumerable.Empty<string>())
                .Select(ToRegex)
                .ToList();
        }

        public bool IsPreserved(string entryName)
        {
            return _names.Contains(entryName) || _patterns.Any(pattern => pattern.IsMatch(entryName));
        }

        public void Preserve(string entryName)
        {
            _names.Add(entryName);
        }

        private static Regex ToRegex(string pattern)
        {
            var expression = new StringBuilder("^");
            foreach (var character in pattern)
            {
                switch (character)
                {
                    case '*':
                        expression.Append(".*");
                        break;
                    case '?':
                        expression.Append('.');
                        break;
                    default:
                        expression.Append(Regex.Escape(character.ToString()));
                        break;
                }
            }

            return new Regex(expression.Append('$').ToString(),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
    }
}
