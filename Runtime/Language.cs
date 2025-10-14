using System.Collections.Generic;

namespace Moonlight.Localization
{
    public static class Language
    {
        public const string English = "en";
        public const string German = "de";
        public const string French = "fr";
        public const string Spanish = "es";
        public const string Italian = "it";
        // public const string Portuguese = "pt";
        public const string Russian = "ru";
        public const string Chinese = "zh";
        public const string Japanese = "ja";
        // public const string Korean = "ko";
        // public const string Arabic = "ar";
        // public const string Hindi = "hi";
        // public const string Turkish = "tr";
        // public const string Dutch = "nl";
        // public const string Polish = "pl";
        // public const string Swedish = "sv";
        // public const string Norwegian = "no";
        // public const string Danish = "da";
        // public const string Finnish = "fi";
        // public const string Greek = "el";
        // public const string Czech = "cs";
        // public const string Hungarian = "hu";
        // public const string Romanian = "ro";
        // public const string Bulgarian = "bg";
        
        public static List<string> SupportedLanguages => new()
        {
            English, French, Spanish, German, Italian, Japanese, Russian, /*Portuguese, , Chinese,
            , Korean, Arabic, Hindi, Turkish, Dutch, Polish, Swedish, Norwegian,
            Danish, Finnish, Greek, Czech, Hungarian, Romanian, Bulgarian*/
        };
    }
}