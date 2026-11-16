using System.Text;

namespace Wonderland
{
    /// <summary>
    /// The body of a side panel (cards, stats, help) as TextMeshPro rich text with a fixed hierarchy: small capital
    /// section headings in the accent colour, then rows of a muted label and a bright value in two columns (a long value
    /// wraps in its own column), or plain lines. Sizes are relative to the panel's fixed body size, never auto-shrunk, so
    /// every card reads the same way: the eye finds the heading, then runs down the labels.
    /// </summary>
    public sealed class CardText
    {
        public const string Accent = "#F4C97A", Muted = "#A8A1B6", Good = "#7BE08A", Warn = "#FFB054", Bad = "#FF6F66";

        private readonly StringBuilder sb = new();
        private readonly int column;
        private bool any;

        /// <param name="labelColumn">Where values start, in percent of the panel's width.</param>
        public CardText(int labelColumn = 33) { column = labelColumn; }

        /// <summary>A section heading ("NOW", "TODAY"...), with a little space above it.</summary>
        public CardText Section(string heading)
        {
            if (any) sb.Append("<size=45%>\n</size>");
            sb.Append($"<size=72%><color={Accent}><b><cspace=0.06em>{heading.ToUpperInvariant()}</cspace></b></color></size>\n");
            any = true;
            return this;
        }

        /// <summary>A label and its value; skipped when the value is empty.</summary>
        public CardText Row(string label, string value, string colour = null)
        {
            if (string.IsNullOrEmpty(value)) return this;
            sb.Append($"<color={Muted}>{label}</color><pos={column}%><indent={column}%>");
            if (colour != null) sb.Append($"<color={colour}>{value}</color>"); else sb.Append(value);
            sb.Append("</indent>\n");
            any = true;
            return this;
        }

        /// <summary>A line of its own (muted when <paramref name="quiet"/>).</summary>
        public CardText Line(string text, bool quiet = false)
        {
            if (string.IsNullOrEmpty(text)) return this;
            sb.Append(quiet ? $"<color={Muted}>{text}</color>\n" : text + "\n");
            any = true;
            return this;
        }

        public override string ToString() => sb.ToString().TrimEnd('\n');

        /// <summary>Colours a word (e.g. "Open" green).</summary>
        public static string Tint(string text, string colour) => $"<color={colour}>{text}</color>";
    }
}
