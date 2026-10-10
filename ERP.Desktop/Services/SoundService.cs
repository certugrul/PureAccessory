using System.Media;

namespace ERP.Desktop.Services;

public static class SoundService
{
    public static void PlayBeep()
    {
        try
        {
            if (ThemeService.Current?.SoundEffectsEnabled != false)
            {
                SystemSounds.Beep.Play();
            }
        }
        catch { }
    }

    public static void PlaySuccess()
    {
        try
        {
            if (ThemeService.Current?.SoundEffectsEnabled != false)
            {
                SystemSounds.Asterisk.Play();
            }
        }
        catch { }
    }

    public static void PlayWarning()
    {
        try
        {
            if (ThemeService.Current?.SoundEffectsEnabled != false)
            {
                SystemSounds.Exclamation.Play();
            }
        }
        catch { }
    }
}
