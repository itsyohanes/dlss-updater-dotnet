using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using dlss_updater.Services;
using Xunit;

namespace DlssUpdater.Tests
{
    public class TranslatorTests
    {
        private static readonly Regex Placeholder = new Regex(@"(?<!\{)\{(\d+)(?:[,:][^}]*)?\}");

        private static Dictionary<string, Dictionary<string, string>> Tables
        {
            get
            {
                FieldInfo field = typeof(Translator).GetField(
                    "Tables",
                    BindingFlags.NonPublic | BindingFlags.Static);
                Assert.NotNull(field);
                return (Dictionary<string, Dictionary<string, string>>)field.GetValue(null);
            }
        }

        public static IEnumerable<object[]> TranslatedLanguages()
        {
            return Translator.Languages
                .Where(language => language.Code != "en")
                .Select(language => new object[] { language.Code });
        }

        [Fact]
        public void LanguageMenuMatchesTranslationTables()
        {
            Assert.Equal(
                Tables.Keys.OrderBy(code => code),
                Translator.Languages.Select(language => language.Code).OrderBy(code => code));
        }

        [Theory]
        [MemberData(nameof(TranslatedLanguages))]
        public void TranslationHasEveryEnglishKey(string code)
        {
            Dictionary<string, string> english = Tables["en"];
            Dictionary<string, string> table = Tables[code];

            Assert.Empty(english.Keys.Except(table.Keys));
            Assert.Empty(table.Keys.Except(english.Keys));
        }

        [Theory]
        [MemberData(nameof(TranslatedLanguages))]
        public void TranslationUsesSamePlaceholdersAsEnglish(string code)
        {
            Dictionary<string, string> english = Tables["en"];
            List<string> mismatches = new List<string>();

            foreach (KeyValuePair<string, string> entry in Tables[code])
            {
                string englishValue;
                if (!english.TryGetValue(entry.Key, out englishValue))
                {
                    continue;
                }

                if (!GetPlaceholders(englishValue).SetEquals(GetPlaceholders(entry.Value)))
                {
                    mismatches.Add(entry.Key);
                }
            }

            Assert.Empty(mismatches);
        }

        [Theory]
        [MemberData(nameof(TranslatedLanguages))]
        [InlineData("en")]
        public void EveryStringFormatsWithoutThrowing(string code)
        {
            List<string> failures = new List<string>();

            foreach (KeyValuePair<string, string> entry in Tables[code])
            {
                try
                {
                    string.Format(entry.Value, "a", "b", "c", "d", "e", "f", "g", "h");
                }
                catch (FormatException)
                {
                    failures.Add(entry.Key);
                }
            }

            Assert.Empty(failures);
        }

        [Theory]
        [MemberData(nameof(TranslatedLanguages))]
        [InlineData("en")]
        public void NoTranslationIsEmpty(string code)
        {
            Assert.Empty(Tables[code].Where(entry => string.IsNullOrWhiteSpace(entry.Value)).Select(entry => entry.Key));
        }

        [Fact]
        public void UnknownKeyFallsBackToTheKey()
        {
            Assert.Equal("NoSuchKeyForTests", Translator.T("NoSuchKeyForTests"));
        }

        private static HashSet<string> GetPlaceholders(string value)
        {
            return new HashSet<string>(
                Placeholder.Matches(value).Cast<Match>().Select(match => match.Groups[1].Value));
        }
    }
}
