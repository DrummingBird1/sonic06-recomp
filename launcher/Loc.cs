using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace Sonic06Launcher
{
    /// <summary>
    /// The launcher's strings, one JSON file per language in Strings\ (embedded
    /// as strings.&lt;code&gt;.json). A key missing from a translation falls back to
    /// English, so a partial translation still works. Switching raises
    /// "Item[]", and every {l:T key} binding updates in place.
    /// </summary>
    public class Loc : INotifyPropertyChanged
    {
        public static Loc I { get; } = new Loc();

        public static readonly (string Code, string Native)[] Languages =
        {
            ("en", "English"), ("he", "עברית"), ("es", "Español"), ("fr", "Français"), ("de", "Deutsch"),
            ("it", "Italiano"), ("pt-BR", "Português (Brasil)"), ("ja", "日本語"), ("ru", "Русский"), ("ar", "العربية"),
        };

        Dictionary<string, string> cur = new Dictionary<string, string>(), en;
        public event PropertyChangedEventHandler PropertyChanged;
        public string Code { get; private set; } = "en";
        public FlowDirection Flow => Code == "he" || Code == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        Loc() { en = Load("en"); cur = en; }

        static Dictionary<string, string> Load(string code)
        {
            using (var s = typeof(Loc).Assembly.GetManifestResourceStream($"strings.{code}.json"))
            {
                if (s == null) return new Dictionary<string, string>();
                using (var r = new StreamReader(s))
                    return new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(r.ReadToEnd());
            }
        }

        public string this[string key] =>
            cur.TryGetValue(key, out var v) ? v : en.TryGetValue(key, out v) ? v : key;

        public string F(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, this[key], args);

        /// <summary>The English text, for logs and bug reports that maintainers read.</summary>
        public string En(string key, string arg = null)
        {
            var t = en.TryGetValue(key, out var v) ? v : key;
            return arg == null ? t : string.Format(CultureInfo.InvariantCulture, t, arg);
        }

        public void Set(string code)
        {
            if (!Languages.Any(l => l.Code == code)) code = "en";
            Code = code;
            cur = code == "en" ? en : Load(code);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Flow)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Code)));
        }

        /// <summary>The Windows display language, if the launcher has it; English otherwise.</summary>
        public static string SystemDefault()
        {
            var c = CultureInfo.CurrentUICulture;
            if (Languages.Any(l => l.Code == c.Name)) return c.Name;
            if (c.TwoLetterISOLanguageName == "pt") return "pt-BR";
            var two = c.TwoLetterISOLanguageName == "iw" ? "he" : c.TwoLetterISOLanguageName;
            return Languages.Any(l => l.Code == two) ? two : "en";
        }
    }

    /// <summary>{l:T key}: a binding to the current language's string for key.</summary>
    public class T : MarkupExtension
    {
        public string Key { get; set; }
        public T() { }
        public T(string key) { Key = key; }
        public override object ProvideValue(IServiceProvider sp) =>
            new Binding($"[{Key}]") { Source = Loc.I, Mode = BindingMode.OneWay }.ProvideValue(sp);
    }

    /// <summary>A choice in a list: the value saved, and its label in the current language.</summary>
    public class Option : INotifyPropertyChanged
    {
        public object Value { get; }
        readonly string key, arg;
        public event PropertyChangedEventHandler PropertyChanged;
        public Option(object value, string key, string arg = null)
        {
            Value = value; this.key = key; this.arg = arg;
            Loc.I.PropertyChanged += (s, e) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
        }
        public string Label => key == null ? arg : arg == null ? Loc.I[key] : Loc.I.F(key, arg);
        public override string ToString() => Label;
    }
}
