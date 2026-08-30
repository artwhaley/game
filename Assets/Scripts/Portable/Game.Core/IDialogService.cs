using System;
using System.Threading;
using System.Threading.Tasks;

namespace TruthCardGame.Core
{
    /// <summary>
    /// Portable host boundary for presenting authored dialog text (Docs/
    /// ToyPatternDialog/DIALOG-HOST-BOUNDARY.md). Direct Dialog and Dialog
    /// From Tags both present through this service; the temporary dialog-via-
    /// cutscene fixture is retired. Hosts implement presentation semantics
    /// (WPF: display + log, complete immediately after presentation).
    /// </summary>
    public interface IDialogService
    {
        /// <summary>Presents one dialog line to the player; completes when presentation is accepted by the host.</summary>
        Task ShowAsync(string text, CancellationToken cancellationToken);
    }
}
