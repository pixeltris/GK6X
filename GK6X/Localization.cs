using MiniJSON;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GK6X
{
    public static class Localization
    {
        public static Dictionary<string, Dictionary<string, string>> Values;
        public static string CurrentLocale = "en";

        public static bool Load()
        {
            Values = new Dictionary<string, Dictionary<string, string>>();

            string dir = Path.Combine(Program.DataBasePath, "i18n", "langs");
            foreach (string file in Directory.GetFiles(dir, "*.json"))
            {
                string locale = Path.GetFileNameWithoutExtension(file);

                Dictionary<string, string> localeValues = new Dictionary<string, string>();
                Values[locale] = localeValues;

                Dictionary<string, object> groups = Json.Deserialize(File.ReadAllText(file)) as Dictionary<string, object>;
                if (groups != null)
                {
                    foreach (KeyValuePair<string, object> group in groups)
                    {
                        string groupName = group.Key;
                        Dictionary<string, object> groupValues = group.Value as Dictionary<string, object>;
                        if (groupValues != null)
                        {
                            foreach (KeyValuePair<string, object> value in groupValues)
                            {
                                if (localeValues.ContainsKey(value.Key))
                                {
                                    Console.WriteLine("[WARNING] Duplicate locale key " + value.Key);
                                }
                                localeValues[value.Key] = value.Value.ToString();
                            }
                        }
                    }
                }
            }

            return true;
        }

        public static string GetValue(string key)
        {
            return GetValue(key, CurrentLocale);
        }

        public static string GetValue(string key, string locale)
        {
            string result;
            TryGetValue(key, out result, locale);
            return result;
        }

        public static bool TryGetValue(string key, out string value)
        {
            return TryGetValue(key, out value, CurrentLocale);
        }

        public static bool TryGetValue(string key, out string value, string locale)
        {
            Dictionary<string, string> localeValues;
            if (Values.TryGetValue(locale, out localeValues))
            {
                return localeValues.TryGetValue(key, out value);
            }
            value = null;
            return false;
        }
    }

    public class LocalizedString
    {
        public string KeyName;

        public string Value
        {
            get { return GetValue(); }
        }

        public LocalizedString(string keyName)
        {
            KeyName = keyName;
        }

        public string GetValue()
        {
            return Localization.GetValue(KeyName);
        }

        public string GetValue(string locale)
        {
            return Localization.GetValue(KeyName, locale);
        }

        public bool TryGetValue(out string value)
        {
            return Localization.TryGetValue(KeyName, out value);
        }

        public bool TryGetValue(out string value, string locale)
        {
            return Localization.TryGetValue(KeyName, out value, locale);
        }
    }
}
