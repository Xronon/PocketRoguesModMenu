using System;
using System.Globalization;
using I2.Loc;
using UnityEngine;

namespace PocketRoguesCheats
{
    /// <summary>
    /// Язык своего текста мода (с 1.8, перевод на английский). Мод идёт за игрой: игра по-русски —
    /// мод по-русски, на любом другом языке игры — по-английски. Настройка «Язык мода» может это
    /// переопределить. Названия героев, характеристик, качества, вещей и описания эффектов мод
    /// берёт у самой игры — они на любом её языке, не только на двух.
    /// </summary>
    internal static class L
    {
        internal const string Auto = "Auto";
        internal const string English = "English";
        internal const string Russian = "Russian";

        private static int _frame = -1;
        private static bool _ru;

        /// <summary>Писать ли по-русски. Узнаём раз за кадр: язык игры меняется в её настройках на ходу.</summary>
        internal static bool Ru
        {
            get
            {
                if (_frame != Time.frameCount)
                {
                    _frame = Time.frameCount;
                    _ru = Detect();
                }
                return _ru;
            }
        }

        private static bool Detect()
        {
            string mode = CheatsPlugin.Language != null ? CheatsPlugin.Language.Value : Auto;
            if (mode == Russian) return true;
            if (mode == English) return false;
            try
            {
                string code = LocalizationManager.CurrentLanguageCode;
                return !string.IsNullOrEmpty(code) && code.StartsWith("ru", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }

        /// <summary>Своя надпись: по-русски или по-английски.</summary>
        internal static string T(string ru, string en)
        {
            return Ru ? ru : en;
        }

        /// <summary>Слово из перевода самой игры (на её языке); нет такого — своя пара.</summary>
        internal static string Game(string term, string ru, string en)
        {
            string t = Translate(term);
            return t.Length > 0 ? t : T(ru, en);
        }

        internal static string Translate(string term)
        {
            try
            {
                string t = LocalizationManager.GetTranslation(term);
                return string.IsNullOrEmpty(t) ? "" : t;
            }
            catch (Exception) { return ""; }
        }

        /// <summary>Дробное число: «2,5» по-русски, «2.5» по-английски.</summary>
        internal static string Num(float v, string format)
        {
            string s = v.ToString(format, CultureInfo.InvariantCulture);
            return Ru ? s.Replace('.', ',') : s;
        }
    }
}
