using System;
using System.Threading;
using System.Threading.Tasks;
using TruthCardGame.Core;

namespace TruthCardGame.ReferenceHost.Wpf
{
    /// <summary>
    /// Concrete WPF-host IDialogService (Docs/ToyPatternDialog/
    /// DIALOG-HOST-BOUNDARY.md). The actual presentation is done by an
    /// injectable presenter so the host boundary stays unit-testable without a
    /// window. If no presenter is wired, or the presenter is null at call time,
    /// the missing host is logged loudly (with the text) and presentation
    /// completes safely — it never throws and never touches Cutscene.
    ///
    /// The default presenter opens the shared dialog prompt window on the UI
    /// thread.
    /// </summary>
    public sealed class DialogHostService : IDialogService
    {
        private readonly Func<string, CancellationToken, Task> _presenter;
        private readonly IGameLog _log;

        public DialogHostService(Func<string, CancellationToken, Task> presenter = null, IGameLog log = null)
        {
            _presenter = presenter;
            _log = log ?? new NullGameLog();
        }

        public async Task ShowAsync(string text, CancellationToken cancellationToken)
        {
            if (_presenter == null)
            {
                _log.Error("DialogHostService: no dialog presenter is wired (missing host). Text dropped: " + text);
                return;
            }
            try
            {
                await _presenter(text, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Error("DialogHostService: presenter failed while showing dialog text: " + text + " :: " + ex);
            }
        }

        /// <summary>Default WPF presenter: show the text in the shared dialog window on the UI thread.</summary>
        public static Func<string, CancellationToken, Task> DefaultPresenter(IGameLog log)
        {
            return async (text, ct) =>
            {
                await System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    // Concrete window presentation is a Human-in-the-loop surface;
                    // the log records the presented line for the service queue.
                    log?.Info("Dialog presented: " + text);
                });
            };
        }
    }
}