using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;

namespace GMCMOptions;

public interface IGMCMOptionsAPI
{
    void AddColorOption(
        IManifest mod,
        Func<Color> getValue,
        Action<Color> setValue,
        Func<string> name,
        Func<string>? tooltip = null,
        bool showAlpha = true,
        uint colorPickerStyle = 0,
        string? fieldId = null,
        Action<SpriteBatch, int, int, Color>? drawSample = null
    );
}
