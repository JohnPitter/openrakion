using System.Globalization;
using System.Text.RegularExpressions;

namespace RakionLauncher;

/// <summary>
/// Game options gravadas no PersistentSymbols.ini da Serious Engine (porta do app.go do luxview).
/// O INI só carrega o bit de fullscreen (m_bActiveFullScreen); a distinção windowed vs borderless é
/// lembrada num arquivo à parte (display.mode). Tela/mouse/som/gamma são símbolos (INDEX)/(FLOAT).
/// O INI é travado read-only após salvar — senão o engine reescreve o próprio modo de display na saída.
/// </summary>
internal sealed class GameSettings
{
    public int ScreenWidth = 1920, ScreenHeight = 1080;
    public string DisplayMode = WindowMode.Fullscreen;
    public double MouseSensitivity = 1.5;
    public bool InvertMouse, MouseAccel = true;
    public double SoundVolume = 0.8, MusicVolume = 0.6, Gamma = 1.0;

    public static GameSettings Load(string iniPath, string modeFile)
    {
        var s = new GameSettings();
        string c;
        try { c = File.ReadAllText(iniPath); } catch { return s; }
        s.ScreenWidth = IntOf(c, "m_pixScreenWidth", s.ScreenWidth);
        s.ScreenHeight = IntOf(c, "m_pixScreenHeight", s.ScreenHeight);
        bool fs = SymbolValue(c, "m_bActiveFullScreen") == "1";
        string remembered = ReadMode(modeFile);
        s.DisplayMode = remembered != "" ? remembered : (fs ? WindowMode.Fullscreen : WindowMode.Windowed);
        s.MouseSensitivity = FloatOf(c, "inp_fMouseSensitivity", s.MouseSensitivity);
        s.InvertMouse = SymbolValue(c, "inp_bInvertMouse") == "1";
        s.MouseAccel = SymbolValue(c, "inp_bAllowMouseAcceleration") == "1";
        s.SoundVolume = FloatOf(c, "snd_fSoundVolume", s.SoundVolume);
        s.MusicVolume = FloatOf(c, "snd_fMusicVolume", s.MusicVolume);
        s.Gamma = FloatOf(c, "gfx_fGamma", s.Gamma);
        return s;
    }

    public void Save(string iniPath, string modeFile)
    {
        string c = File.ReadAllText(iniPath);
        string mode = Normalize(DisplayMode);
        c = SetSymbol(c, "m_pixScreenWidth", ScreenWidth.ToString());
        c = SetSymbol(c, "m_pixScreenHeight", ScreenHeight.ToString());
        // windowed E borderless rodam o engine WINDOWED (bit=0); o framing (WindowMode) decide qual.
        c = SetSymbol(c, "m_bActiveFullScreen", mode == WindowMode.Fullscreen ? "1" : "0");
        c = SetSymbol(c, "inp_fMouseSensitivity", F(MouseSensitivity));
        c = SetSymbol(c, "inp_bInvertMouse", InvertMouse ? "1" : "0");
        c = SetSymbol(c, "inp_bAllowMouseAcceleration", MouseAccel ? "1" : "0");
        c = SetSymbol(c, "snd_fSoundVolume", F(SoundVolume));
        c = SetSymbol(c, "snd_fMusicVolume", F(MusicVolume));
        c = SetSymbol(c, "gfx_fGamma", F(Gamma));
        SetReadOnly(iniPath, false);   // destrava p/ escrever (pode estar travado de antes)
        File.WriteAllText(iniPath, c);
        SetReadOnly(iniPath, true);    // trava de novo — o engine sobrescreve o modo na saída senão
        WriteMode(modeFile, mode);
    }

    private static string Normalize(string m) =>
        m is WindowMode.Windowed or WindowMode.Borderless or WindowMode.Fullscreen ? m : WindowMode.Fullscreen;

    private static string SymbolValue(string c, string name)
    {
        var m = Regex.Match(c, Regex.Escape(name) + @"=\((?:INDEX|FLOAT)\)([-0-9.eE]+)");
        return m.Success ? m.Groups[1].Value : "";
    }
    private static string SetSymbol(string c, string name, string value) =>
        Regex.Replace(c, "(" + Regex.Escape(name) + @"=\((?:INDEX|FLOAT)\))[-0-9.eE]+", "${1}" + value);
    private static int IntOf(string c, string n, int def) => int.TryParse(SymbolValue(c, n), out var v) ? v : def;
    private static double FloatOf(string c, string n, double def) =>
        double.TryParse(SymbolValue(c, n), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;
    private static string F(double f) => f.ToString("0.######", CultureInfo.InvariantCulture);

    private static string ReadMode(string f)
    {
        try { string m = File.ReadAllText(f).Trim(); return m is WindowMode.Windowed or WindowMode.Borderless or WindowMode.Fullscreen ? m : ""; }
        catch { return ""; }
    }
    private static void WriteMode(string f, string m) { try { File.WriteAllText(f, m); } catch { } }
    private static void SetReadOnly(string p, bool ro)
    {
        try { var a = File.GetAttributes(p); File.SetAttributes(p, ro ? a | FileAttributes.ReadOnly : a & ~FileAttributes.ReadOnly); }
        catch { }
    }
}
