using System;
using TruthCardGame.Content;
using TruthCardGame.Content.Sqlite;

namespace TruthCardGame.ReferenceHost.Wpf
{
    /// <summary>One undoable edit for the currently dirty Card buffer.</summary>
    internal sealed class InMemorySequenceCommand : IAuthoringCommand
    {
        private readonly Action<ActionSequenceDefinition> _write;
        private readonly ActionSequenceDefinition _before;
        private readonly ActionSequenceDefinition _after;
        private readonly Action _refresh;

        public InMemorySequenceCommand(string name, Action<ActionSequenceDefinition> write,
            ActionSequenceDefinition before,
            ActionSequenceDefinition after, Action refresh)
        {
            Name = name; _write = write; _before = before; _after = after; _refresh = refresh;
        }

        public string Name { get; }
        public string MergeKey => null;
        public bool Merge(IAuthoringCommand incoming) => false;
        public void Execute() { _write(SequenceSnapshotUtility.Clone(_after)); _refresh?.Invoke(); }
        public void Undo() { _write(SequenceSnapshotUtility.Clone(_before)); _refresh?.Invoke(); }
    }
}
