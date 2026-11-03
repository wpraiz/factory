namespace Factory.Services;

/// <summary>
/// Navegacao entre secoes disparada de dentro das paginas ("ir pro projeto X", "ir pra GRANA").
/// MainWindow troca a secao; ProjectsViewModel seleciona o projeto. Tudo na thread de UI.
/// </summary>
public sealed class NavigationService
{
    public event EventHandler<string>? ProjectRequested;
    public event EventHandler<string>? SectionRequested;

    public void GoToProject(string path) => ProjectRequested?.Invoke(this, path);

    /// <summary>Tag da secao como em <see cref="Navigation.SectionRegistry"/> (projects, search, contexts, money, forge).</summary>
    public void GoToSection(string tag) => SectionRequested?.Invoke(this, tag);
}
