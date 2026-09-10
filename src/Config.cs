using Microsoft.Xna.Framework;

namespace HeartsOverflow;

sealed class Config {
    public bool NpcOverflowHearts { get; set; } = true;

    public bool AnimalOverflowHearts { get; set; } = true;

    public bool SortByTotalFriendship { get; set; } = true;

    public TextColor? TextColorOverride { get; set; } = null;

    internal sealed class TextColor {
        public TextColor(byte r, byte g, byte b, byte a) {
            this.R = r;
            this.G = g;
            this.B = b;
            this.A = a;
        }

        public byte R { get; set; }
        public byte G { get; set; }
        public byte B { get; set; }
        public byte A { get; set; }

        internal Color AsColor() => new(this.R, this.G, this.B, this.A);

        internal TextColor(Color color) => this.SetColor(color);

        internal void SetColor(Color color) {
            this.R = color.R;
            this.G = color.G;
            this.B = color.B;
            this.A = color.A;
        }
    }
}
