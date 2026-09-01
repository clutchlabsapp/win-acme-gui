namespace WinAcmeGui;

/// <summary>
/// Scaffolding shell. The real layout arrives with the phase 1 UI commit.
/// </summary>
public sealed class MainForm : Form
{
    public MainForm()
    {
        Text = "win-acme GUI";
        Width = 900;
        Height = 700;
        StartPosition = FormStartPosition.CenterScreen;
    }
}
