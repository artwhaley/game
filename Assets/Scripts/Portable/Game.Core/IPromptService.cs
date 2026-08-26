using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TruthCardGame.Core
{
    public interface IPromptService
    {
        /// <summary>Shows a choice. Returns the chosen index, or null when dismissed/no valid choice.</summary>
        Task<int?> AskAsync(string prompt, IReadOnlyList<string> options, CancellationToken cancellationToken);
    }
}
