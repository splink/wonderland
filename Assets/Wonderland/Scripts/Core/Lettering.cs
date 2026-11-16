using System.Collections.Generic;
using UnityEngine;

namespace Wonderland
{
    /// <summary>
    /// Block letters for signs: a 5 x 7 pixel font built from little boxes (each row's run of pixels is one box), so a
    /// word on a shop's board, a neon sign or a station name board is real geometry that reads from across the room and
    /// glows at night like the windows (colour alpha is the emission mask: 0 glows fully).
    /// </summary>
    public static class Lettering
    {
        private static readonly Dictionary<char, string[]> Glyphs = new()
        {
            ['A'] = new[] { " ### ", "#   #", "#   #", "#####", "#   #", "#   #", "#   #" },
            ['B'] = new[] { "#### ", "#   #", "#   #", "#### ", "#   #", "#   #", "#### " },
            ['C'] = new[] { " ### ", "#   #", "#    ", "#    ", "#    ", "#   #", " ### " },
            ['D'] = new[] { "#### ", "#   #", "#   #", "#   #", "#   #", "#   #", "#### " },
            ['E'] = new[] { "#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#####" },
            ['F'] = new[] { "#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#    " },
            ['G'] = new[] { " ### ", "#   #", "#    ", "# ###", "#   #", "#   #", " ### " },
            ['H'] = new[] { "#   #", "#   #", "#   #", "#####", "#   #", "#   #", "#   #" },
            ['I'] = new[] { " ### ", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", " ### " },
            ['J'] = new[] { "  ###", "   # ", "   # ", "   # ", "   # ", "#  # ", " ##  " },
            ['K'] = new[] { "#   #", "#  # ", "# #  ", "##   ", "# #  ", "#  # ", "#   #" },
            ['L'] = new[] { "#    ", "#    ", "#    ", "#    ", "#    ", "#    ", "#####" },
            ['M'] = new[] { "#   #", "## ##", "# # #", "# # #", "#   #", "#   #", "#   #" },
            ['N'] = new[] { "#   #", "##  #", "# # #", "#  ##", "#   #", "#   #", "#   #" },
            ['O'] = new[] { " ### ", "#   #", "#   #", "#   #", "#   #", "#   #", " ### " },
            ['P'] = new[] { "#### ", "#   #", "#   #", "#### ", "#    ", "#    ", "#    " },
            ['Q'] = new[] { " ### ", "#   #", "#   #", "#   #", "# # #", "#  # ", " ## #" },
            ['R'] = new[] { "#### ", "#   #", "#   #", "#### ", "# #  ", "#  # ", "#   #" },
            ['S'] = new[] { " ####", "#    ", "#    ", " ### ", "    #", "    #", "#### " },
            ['T'] = new[] { "#####", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", "  #  " },
            ['U'] = new[] { "#   #", "#   #", "#   #", "#   #", "#   #", "#   #", " ### " },
            ['V'] = new[] { "#   #", "#   #", "#   #", "#   #", "#   #", " # # ", "  #  " },
            ['W'] = new[] { "#   #", "#   #", "#   #", "# # #", "# # #", "## ##", "#   #" },
            ['X'] = new[] { "#   #", "#   #", " # # ", "  #  ", " # # ", "#   #", "#   #" },
            ['Y'] = new[] { "#   #", "#   #", " # # ", "  #  ", "  #  ", "  #  ", "  #  " },
            ['Z'] = new[] { "#####", "    #", "   # ", "  #  ", " #   ", "#    ", "#####" },
            ['0'] = new[] { " ### ", "#   #", "#  ##", "# # #", "##  #", "#   #", " ### " },
            ['1'] = new[] { "  #  ", " ##  ", "  #  ", "  #  ", "  #  ", "  #  ", " ### " },
            ['2'] = new[] { " ### ", "#   #", "    #", "   # ", "  #  ", " #   ", "#####" },
            ['3'] = new[] { "#### ", "    #", "    #", " ### ", "    #", "    #", "#### " },
            ['4'] = new[] { "   # ", "  ## ", " # # ", "#  # ", "#####", "   # ", "   # " },
            ['5'] = new[] { "#####", "#    ", "#### ", "    #", "    #", "#   #", " ### " },
            ['6'] = new[] { " ### ", "#    ", "#    ", "#### ", "#   #", "#   #", " ### " },
            ['7'] = new[] { "#####", "    #", "   # ", "  #  ", " #   ", " #   ", " #   " },
            ['8'] = new[] { " ### ", "#   #", "#   #", " ### ", "#   #", "#   #", " ### " },
            ['9'] = new[] { " ### ", "#   #", "#   #", " ####", "    #", "    #", " ### " },
            ['-'] = new[] { "     ", "     ", "     ", " ### ", "     ", "     ", "     " },
            ['.'] = new[] { "     ", "     ", "     ", "     ", "     ", "     ", "  #  " },
            ['&'] = new[] { " ##  ", "#  # ", "# #  ", " #   ", "# # #", "#  # ", " ## #" },
            ['!'] = new[] { "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", "     ", "  #  " },
            ['+'] = new[] { "     ", "  #  ", "  #  ", "#####", "  #  ", "  #  ", "     " },
            [' '] = new[] { "     ", "     ", "     ", "     ", "     ", "     ", "     " },
        };

        /// <summary>How wide a word is at this letter height (five pixels a letter, one between).</summary>
        public static float Width(string text, float height) => (text.Length * 6 - 1) * height / 7f;

        /// <summary>
        /// Adds <paramref name="text"/> centred on <paramref name="centre"/>, reading from the front: the letters stand
        /// <paramref name="depth"/> proud of the surface whose outward normal is <paramref name="facing"/>.
        /// </summary>
        public static void Add(MeshBuilder b, string text, Vector3 centre, Vector3 facing, float height, float depth, Color colour)
        {
            for (int i = 0; i < text.Length; i++) AddLetter(b, text, i, centre, facing, height, depth, colour);
        }

        /// <summary>Just letter <paramref name="index"/> of <paramref name="text"/> (placed as in <see cref="Add"/>): for neon that lights letter by letter.</summary>
        public static void AddLetter(MeshBuilder b, string text, int index, Vector3 centre, Vector3 facing, float height, float depth, Color colour)
        {
            float px = height / 7f, x0 = -Width(text, height) / 2f;
            b.Push(centre, Quaternion.LookRotation(-facing, Vector3.up));   // local x: along the reading; y: up; z: into the surface
            if (Glyphs.TryGetValue(char.ToUpperInvariant(text[index]), out var rows))
            {
                float left = x0 + index * 6 * px;
                for (int r = 0; r < 7; r++)
                {
                    string row = rows[r];
                    for (int c = 0; c < 5; c++)
                    {
                        if (row[c] != '#') continue;
                        int run = 1; while (c + run < 5 && row[c + run] == '#') run++;
                        b.AddBox(new Vector3(left + (c + run * 0.5f) * px, (3f - r) * px, -depth * 0.5f), new Vector3(run * px * 0.98f, px * 0.98f, depth), colour);
                        c += run - 1;
                    }
                }
            }
            b.Pop();
        }

        /// <summary>
        /// A name board: a painted panel with a frame and raised letters, sized to the word, standing at
        /// <paramref name="centre"/> and facing <paramref name="facing"/>. Letters glow softly at night.
        /// </summary>
        public static void Board(MeshBuilder b, string text, Vector3 centre, Vector3 facing, float height, Color board, Color letters, Color frame)
        {
            float w = Width(text, height) + height * 0.9f, h = height * 1.75f;
            b.Push(centre, Quaternion.LookRotation(-facing, Vector3.up));
            b.AddBox(new Vector3(0f, 0f, 0.0012f), new Vector3(w, h, 0.0024f), board, Pal.Shade(board, 1.05f), Pal.Shade(board, 0.8f));
            b.AddBox(new Vector3(0f, h / 2f, 0.0012f), new Vector3(w + 0.0016f, 0.0012f, 0.0034f), frame);
            b.AddBox(new Vector3(0f, -h / 2f, 0.0012f), new Vector3(w + 0.0016f, 0.0012f, 0.0034f), frame);
            foreach (int s in new[] { -1, 1 }) b.AddBox(new Vector3(s * w / 2f, 0f, 0.0012f), new Vector3(0.0012f, h + 0.0016f, 0.0034f), frame);
            b.Pop();
            var glow = letters; glow.a = 0.55f;   // a soft glow after dark, like a lit shop sign
            Add(b, text, centre - facing.normalized * 0.0001f, facing, height, 0.0009f, glow);
        }
    }
}
