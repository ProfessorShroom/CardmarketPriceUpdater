using System.Globalization;
using System.Text;

// Copyright © Charlie Howard 2025-2026 All rights reserved

namespace Cardmarket_Price_Updater.Core
{
    public enum GameType
    {
        MTG,
        Pokemon,
        YuGiOh
    }

    public static class GameTypeExtensions
    {
        /// <summary>The game used when nothing else says otherwise.</summary>
        public const GameType Default = GameType.YuGiOh;

        public static string DisplayName(this GameType game) => game switch
        {
            GameType.MTG => "Magic: The Gathering",
            GameType.Pokemon => "Pokémon",
            GameType.YuGiOh => "Yu-Gi-Oh!",
            _ => game.ToString()
        };

        /// <summary>
        /// Loosely parses a game name from a spreadsheet cell or CLI argument.
        /// Accepts things like "MTG", "Magic", "Magic: The Gathering", "Pokemon", "Pokémon",
        /// "YGO", "Yugioh", "Yu-Gi-Oh!" (case/punctuation/accents ignored).
        /// Returns null if blank or not recognised.
        /// </summary>
        public static GameType? ParseGame(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            // lower-case, strip accents (é -> e), keep letters/digits only
            var sb = new StringBuilder();
            foreach (char ch in text.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                    continue;
                if (char.IsLetterOrDigit(ch))
                    sb.Append(char.ToLowerInvariant(ch));
            }
            string s = sb.ToString();

            if (s == "m" || s == "mtg" || s.Contains("magic"))
                return GameType.MTG;
            if (s == "p" || s == "pkmn" || s.Contains("pokemon"))
                return GameType.Pokemon;
            if (s == "y" || s == "ygo" || s.Contains("yugioh"))
                return GameType.YuGiOh;

            return null;
        }
    }
}
