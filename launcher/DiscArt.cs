using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Sonic06Launcher
{
    /// <summary>
    /// The pictures on the player's own disc: PS3_GAME\PIC0.PNG (Sonic with the
    /// logo, 1000x560) for the hero card and ICON0.PNG (the logo, 320x176) for the
    /// sidebar. The repository holds no artwork of the game; until the disc is
    /// copied, the launcher draws its own colours instead.
    /// </summary>
    static class DiscArt
    {
        public static ImageSource Hero => Load("PIC0.PNG");
        public static ImageSource Logo => Load("ICON0.PNG");

        static ImageSource Load(string name)
        {
            var file = Path.Combine(Workspace.Disc, "PS3_GAME", name);
            if (!File.Exists(file)) return null;
            try
            {
                var b = new BitmapImage();
                b.BeginInit();
                b.CacheOption = BitmapCacheOption.OnLoad;   // read it now, so the file is not held open
                b.UriSource = new Uri(file);
                b.EndInit();
                b.Freeze();
                return b;
            }
            catch (Exception) { return null; }   // a damaged picture is not a reason to stop the launcher
        }
    }
}
