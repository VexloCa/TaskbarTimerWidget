using System;
using System.Collections.Generic;
using System.Drawing;

namespace TaskbarTimerWidget
{
    internal enum WidgetColorCategory
    {
        Default,
        Solid,
        Gradient
    }

    internal sealed class WidgetColorOption
    {
        public WidgetColorOption(string id, string name, WidgetColorCategory category, Color color1, Color color2, Color textColor, float angle = 45f)
        {
            Id = id;
            Name = name;
            Category = category;
            Color1 = color1;
            Color2 = color2;
            TextColor = textColor;
            Angle = angle;
        }

        public string Id { get; private set; }
        public string Name { get; private set; }
        public WidgetColorCategory Category { get; private set; }
        public Color Color1 { get; private set; }
        public Color Color2 { get; private set; }
        public Color TextColor { get; private set; }
        public float Angle { get; private set; }

        public bool IsGradient
        {
            get { return Category == WidgetColorCategory.Gradient && Color1 != Color2; }
        }

        public override string ToString()
        {
            return Name;
        }
    }

    internal static class WidgetColorOptions
    {
        public static readonly WidgetColorOption DefaultOption = new WidgetColorOption(
            "Default",
            "Default (System Theme)",
            WidgetColorCategory.Default,
            Color.Empty,
            Color.Empty,
            Color.Empty);

        private static readonly WidgetColorOption[] options = new WidgetColorOption[]
        {
            DefaultOption,

            // Solid Colors
            new WidgetColorOption("DarkSlate", "Dark Slate", WidgetColorCategory.Solid, Color.FromArgb(30, 34, 42), Color.FromArgb(30, 34, 42), Color.FromArgb(220, 225, 235)),
            new WidgetColorOption("MidnightBlue", "Midnight Blue", WidgetColorCategory.Solid, Color.FromArgb(15, 23, 42), Color.FromArgb(15, 23, 42), Color.FromArgb(200, 215, 240)),
            new WidgetColorOption("DeepPurple", "Deep Purple", WidgetColorCategory.Solid, Color.FromArgb(55, 20, 110), Color.FromArgb(55, 20, 110), Color.FromArgb(220, 200, 255)),
            new WidgetColorOption("ForestGreen", "Forest Green", WidgetColorCategory.Solid, Color.FromArgb(10, 75, 55), Color.FromArgb(10, 75, 55), Color.FromArgb(180, 240, 210)),
            new WidgetColorOption("CrimsonRed", "Crimson Red", WidgetColorCategory.Solid, Color.FromArgb(130, 20, 50), Color.FromArgb(130, 20, 50), Color.FromArgb(255, 210, 220)),
            new WidgetColorOption("PureBlack", "Pure Black", WidgetColorCategory.Solid, Color.FromArgb(10, 10, 10), Color.FromArgb(10, 10, 10), Color.FromArgb(230, 230, 230)),
            new WidgetColorOption("LightSilver", "Light Silver", WidgetColorCategory.Solid, Color.FromArgb(238, 242, 248), Color.FromArgb(238, 242, 248), Color.FromArgb(20, 30, 50)),

            // Gradient Options
            new WidgetColorOption("Sunset", "Sunset", WidgetColorCategory.Gradient, Color.FromArgb(200, 60, 50), Color.FromArgb(170, 45, 85), Color.White, 135f),
            new WidgetColorOption("OceanBreeze", "Ocean Breeze", WidgetColorCategory.Gradient, Color.FromArgb(25, 100, 150), Color.FromArgb(40, 130, 175), Color.White, 135f),
            new WidgetColorOption("NeonEmerald", "Emerald", WidgetColorCategory.Gradient, Color.FromArgb(15, 120, 95), Color.FromArgb(25, 155, 110), Color.White, 135f),
            new WidgetColorOption("CosmicPurple", "Cosmic Purple", WidgetColorCategory.Gradient, Color.FromArgb(85, 30, 160), Color.FromArgb(110, 50, 185), Color.White, 135f),
            new WidgetColorOption("FireIce", "Steel Blue", WidgetColorCategory.Gradient, Color.FromArgb(40, 75, 130), Color.FromArgb(55, 105, 160), Color.White, 135f),
            new WidgetColorOption("AuroraPink", "Rose", WidgetColorCategory.Gradient, Color.FromArgb(160, 50, 90), Color.FromArgb(185, 70, 110), Color.White, 135f)
        };

        public static IList<WidgetColorOption> All
        {
            get { return Array.AsReadOnly(options); }
        }

        public static WidgetColorOption Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return DefaultOption;

            foreach (WidgetColorOption option in options)
            {
                if (string.Equals(option.Id, id, StringComparison.OrdinalIgnoreCase))
                    return option;
            }

            return DefaultOption;
        }
    }
}
