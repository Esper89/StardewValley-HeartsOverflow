using Microsoft.Xna.Framework;
using StardewValley;

namespace HeartsOverflow;

sealed class Config {
    internal static Config Read(Mod mod) => mod.Helper.ReadConfig<Config>();

    internal static void Register(Mod mod) {
        mod.WithApi<
            GenericModConfigMenu.IGenericModConfigMenuApi
        >("spacechase0.GenericModConfigMenu", gmcm => {
            gmcm.Register(
                mod: mod.ModManifest,
                reset: () => mod.Config = new Config(),
                save: () => mod.Helper.WriteConfig(mod.Config)
            );
            gmcm.AddBoolOption(
                mod: mod.ModManifest,
                getValue: () => mod.Config.NpcOverflowHearts,
                setValue: value => mod.Config.NpcOverflowHearts = value,
                name: () => mod.Helper.Translation.Get("config.npc-overflow-hearts.name"),
                tooltip: () => mod.Helper.Translation.Get("config.npc-overflow-hearts.desc")
            );
            gmcm.AddBoolOption(
                mod: mod.ModManifest,
                getValue: () => mod.Config.AnimalOverflowHearts,
                setValue: value => mod.Config.AnimalOverflowHearts = value,
                name: () => mod.Helper.Translation.Get("config.animal-overflow-hearts.name"),
                tooltip: () => mod.Helper.Translation.Get("config.animal-overflow-hearts.desc")
            );
            gmcm.AddBoolOption(
                mod: mod.ModManifest,
                getValue: () => mod.Config.SortByTotalFriendship,
                setValue: value => mod.Config.SortByTotalFriendship = value,
                name: () => mod.Helper.Translation.Get("config.sort-by-total-friendship.name"),
                tooltip: () => mod.Helper.Translation.Get("config.sort-by-total-friendship.desc")
            );

            mod.WithApi<GMCMOptions.IGMCMOptionsAPI>("jltaylor-us.GMCMOptions", gmcmOpts => {
                gmcm.AddBoolOption(
                    mod: mod.ModManifest,
                    getValue: () => mod.Config.TextColorOverride is not null,
                    setValue: value => mod.Config.TextColorOverride = value
                        ? new(Game1.textColor)
                        : null,
                    name: () => mod.Helper.Translation.Get("config.override-text-color.name"),
                    tooltip: () => mod.Helper.Translation.Get("config.override-text-color.desc")
                );
                gmcmOpts.AddColorOption(
                    mod: mod.ModManifest,
                    getValue: () => mod.Config.TextColorOverride?.AsColor() ?? Game1.textColor,
                    setValue: value => mod.Config.TextColorOverride?.SetColor(value),
                    name: () => mod.Helper.Translation.Get("config.text-color-override.name"),
                    tooltip: () => mod.Helper.Translation.Get("config.text-color-override.desc")
                );
            });
        });
    }

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
