using System.Reflection;

namespace RakionLauncher;

/// <summary>Paleta do Softnyx Game Launcher (amostrada do selectgame_frame.bmp) + carga dos assets
/// embutidos. Mantém o look roxo do launcher original, mas só com o Rakion.</summary>
internal static class Theme
{
    public static readonly Color Dark = Color.FromArgb(0x5A, 0x4D, 0x92);        // roxo escuro (header/faixas)
    public static readonly Color Lilac = Color.FromArgb(0x8C, 0x80, 0xC0);       // lilás (fundo)
    public static readonly Color LilacLight = Color.FromArgb(0xC4, 0xBC, 0xE4);  // lilás claro (botões/painéis)
    public static readonly Color Panel = Color.FromArgb(0xF6, 0xF6, 0xFC);       // quase branco (área de status)
    public static readonly Color Ink = Color.FromArgb(0x33, 0x2B, 0x55);         // texto escuro

    // Os assets (ícone/arte) são derivados do cliente e ficam fora do git (ver Assets\README.md); quando
    // ausentes o recurso não é embutido. Devolvemos null e a UI degrada (sem ícone/banner) em vez de quebrar.
    public static Image? Load(string name)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("RakionLauncher.Assets." + name);
        return s != null ? Image.FromStream(s) : null;
    }

    public static Icon? LoadIcon(string name)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("RakionLauncher.Assets." + name);
        return s != null ? new Icon(s) : null;
    }

    /// <summary>Estiliza um botão no visual do launcher (lilás claro, plano, borda roxa).</summary>
    public static void StyleButton(Button b, bool primary = false)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderColor = Dark;
        b.FlatAppearance.BorderSize = 1;
        b.BackColor = primary ? LilacLight : Color.FromArgb(0xD8, 0xD2, 0xEE);
        b.ForeColor = Ink;
        b.Font = new Font("Segoe UI", primary ? 11f : 9f, FontStyle.Bold);
        b.UseVisualStyleBackColor = false;
        b.Cursor = Cursors.Hand;
    }
}
