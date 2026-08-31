using System;
using System.Data.Common;

namespace TruthCardGame.Content.Sqlite
{
    public sealed class CreateActionBlockCommand : AuthoringCommandBase
    {
        private readonly ActionBlockDefinition _block;
        public CreateActionBlockCommand(Func<DbConnection> connection, ActionBlockDefinition block) : base(connection) { _block = block ?? throw new ArgumentNullException(nameof(block)); }
        public override string Name => "Create Action Block";
        protected override void ExecuteCore(DbConnection connection) => ActionBlockRepository.Create(connection, _block);
        protected override void UndoCore(DbConnection connection) => ActionBlockRepository.Delete(connection, _block.Id);
    }

    public sealed class RenameActionBlockCommand : AuthoringCommandBase
    {
        private readonly string _id;
        private readonly string _oldName;
        private string _newName;
        public RenameActionBlockCommand(Func<DbConnection> connection, string id, string oldName, string newName) : base(connection)
        { _id = id; _oldName = oldName; _newName = newName; }
        public override string Name => "Rename Action Block";
        public override string MergeKey => "actionblock-name:" + _id;
        public override bool Merge(IAuthoringCommand incoming)
        {
            var rename = incoming as RenameActionBlockCommand;
            if (rename == null || rename._id != _id) return false;
            _newName = rename._newName;
            return true;
        }
        protected override void ExecuteCore(DbConnection connection) => ActionBlockRepository.Rename(connection, _id, _newName);
        protected override void UndoCore(DbConnection connection) => ActionBlockRepository.Rename(connection, _id, _oldName);
    }

    public sealed class DeleteActionBlockCommand : AuthoringCommandBase
    {
        private readonly ActionBlockDefinition _block;
        public DeleteActionBlockCommand(Func<DbConnection> connection, string id) : base(connection)
        {
            using (var db = connection()) _block = ActionBlockRepository.Get(db, id) ?? throw new InvalidOperationException("Action Block not found: " + id);
        }
        public override string Name => "Delete Action Block";
        protected override void ExecuteCore(DbConnection connection) => ActionBlockRepository.Delete(connection, _block.Id);
        protected override void UndoCore(DbConnection connection) => ActionBlockRepository.Create(connection, _block);
    }

    public sealed class MoveActionBlockFolderCommand : AuthoringCommandBase
    {
        private readonly string _id;
        private readonly string _before;
        private readonly string _after;

        public MoveActionBlockFolderCommand(Func<DbConnection> connection, string id,
            string before, string after) : base(connection)
        {
            _id = id;
            _before = ActionBlockSerializer.NormalizeFolderPath(before);
            _after = ActionBlockSerializer.NormalizeFolderPath(after);
        }

        public override string Name => "Move Action Block";
        protected override void ExecuteCore(DbConnection connection) =>
            ActionBlockRepository.SetFolderPath(connection, _id, _after);
        protected override void UndoCore(DbConnection connection) =>
            ActionBlockRepository.SetFolderPath(connection, _id, _before);
    }
}
