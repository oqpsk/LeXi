using Lexi.Core;

namespace Lexi;

public partial class MainWindow
{
    internal Task LoadPronunciationAsync(string query, CancellationToken token, bool force, bool allowNetwork = true) =>
        session.LoadPronunciationAsync(query, token, force, allowNetwork);
}
