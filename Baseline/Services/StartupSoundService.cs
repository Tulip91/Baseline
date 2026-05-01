using System.IO;
using System.Media;

namespace BaseLine.Services;

public static class StartupSoundService
{
    private static bool StartupSoundEnabled => true;

    public static Task TryPlayAsync()
    {
        if (!StartupSoundEnabled)
        {
            return Task.CompletedTask;
        }

        return Task.Run(() =>
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", "startup.wav");
                if (!File.Exists(path))
                {
                    return;
                }

                using var player = new SoundPlayer(path);
                player.Load();
                player.Play();
            }
            catch
            {
            }
        });
    }
}
