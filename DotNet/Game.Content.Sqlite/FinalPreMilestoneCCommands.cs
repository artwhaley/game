using System;
using System.Collections.Generic;
using System.Data.Common;
using TruthCardGame.Content;

namespace TruthCardGame.Content.Sqlite
{
    /// <summary>Semantic undo commands for the Resource and Dialog catalogs.</summary>
    public sealed class CreateResourceCommand : AuthoringCommandBase, ICatalogMutationCommand
    {
        private readonly ResourceDefinition _resource;
        public CreateResourceCommand(Func<DbConnection> conn, ResourceDefinition resource) : base(conn)
        {
            _resource = Copy(resource);
        }
        public override string Name => "Create resource";
        public string CatalogKind => CatalogKinds.Resource;
        public string CatalogId => _resource.Id;
        public bool DeletesOnExecute => false;
        public bool DeletesOnUndo => true;
        protected override void ExecuteCore(DbConnection connection) => ResourceRepository.Create(connection, Copy(_resource));
        protected override void UndoCore(DbConnection connection) => ResourceRepository.DeleteIfUnused(connection, _resource.Id);
        internal static ResourceDefinition Copy(ResourceDefinition resource) => new ResourceDefinition
        {
            Id = resource?.Id ?? "", Kind = resource?.Kind ?? "", Name = resource?.Name ?? ""
        };
    }

    public sealed class RenameResourceCommand : AuthoringCommandBase
    {
        private readonly string _id;
        private readonly string _oldName;
        private string _newName;
        public RenameResourceCommand(Func<DbConnection> conn, string id, string oldName, string newName) : base(conn)
        {
            _id = id; _oldName = oldName ?? ""; _newName = newName ?? "";
        }
        public override string Name => "Rename resource";
        public override string MergeKey => "resource-name:" + _id;
        public override bool Merge(IAuthoringCommand incoming)
        {
            if (incoming is RenameResourceCommand rename && rename._id == _id)
            {
                _newName = rename._newName;
                return true;
            }
            return false;
        }
        protected override void ExecuteCore(DbConnection connection) => ResourceRepository.Rename(connection, _id, _newName);
        protected override void UndoCore(DbConnection connection) => ResourceRepository.Rename(connection, _id, _oldName);
    }

    public sealed class DeleteResourceCommand : AuthoringCommandBase, ICatalogMutationCommand
    {
        private readonly ResourceDefinition _resource;
        public DeleteResourceCommand(Func<DbConnection> conn, ResourceDefinition resource) : base(conn)
        {
            _resource = CreateResourceCommand.Copy(resource);
        }
        public override string Name => "Delete resource";
        public string CatalogKind => CatalogKinds.Resource;
        public string CatalogId => _resource.Id;
        public bool DeletesOnExecute => true;
        public bool DeletesOnUndo => false;
        protected override void ExecuteCore(DbConnection connection) => ResourceRepository.DeleteIfUnused(connection, _resource.Id);
        protected override void UndoCore(DbConnection connection) => ResourceRepository.Create(connection, CreateResourceCommand.Copy(_resource));
    }

    public sealed class CreateDialogTagCommand : AuthoringCommandBase, ICatalogMutationCommand
    {
        private readonly DialogTagDefinition _tag;
        public CreateDialogTagCommand(Func<DbConnection> conn, DialogTagDefinition tag) : base(conn)
        {
            _tag = Copy(tag);
        }
        public override string Name => "Create dialog tag";
        public string CatalogKind => CatalogKinds.DialogTag;
        public string CatalogId => _tag.Id;
        public bool DeletesOnExecute => false;
        public bool DeletesOnUndo => true;
        protected override void ExecuteCore(DbConnection connection) => DialogCatalogRepository.CreateTag(connection, Copy(_tag));
        protected override void UndoCore(DbConnection connection) => DialogCatalogRepository.DeleteTagIfUnused(connection, _tag.Id);
        internal static DialogTagDefinition Copy(DialogTagDefinition tag) => new DialogTagDefinition
        {
            Id = tag?.Id ?? "", Title = tag?.Title ?? "", SortOrder = tag?.SortOrder ?? 0
        };
    }

    public sealed class RenameDialogTagCommand : AuthoringCommandBase
    {
        private readonly string _id;
        private readonly string _oldTitle;
        private string _newTitle;
        public RenameDialogTagCommand(Func<DbConnection> conn, string id, string oldTitle, string newTitle) : base(conn)
        {
            _id = id; _oldTitle = oldTitle ?? ""; _newTitle = newTitle ?? "";
        }
        public override string Name => "Rename dialog tag";
        public override string MergeKey => "dialog-tag-name:" + _id;
        public override bool Merge(IAuthoringCommand incoming)
        {
            if (incoming is RenameDialogTagCommand rename && rename._id == _id)
            {
                _newTitle = rename._newTitle;
                return true;
            }
            return false;
        }
        protected override void ExecuteCore(DbConnection connection) => DialogCatalogRepository.RenameTag(connection, _id, _newTitle);
        protected override void UndoCore(DbConnection connection) => DialogCatalogRepository.RenameTag(connection, _id, _oldTitle);
    }

    public sealed class DeleteDialogTagCommand : AuthoringCommandBase, ICatalogMutationCommand
    {
        private readonly DialogTagDefinition _tag;
        public DeleteDialogTagCommand(Func<DbConnection> conn, DialogTagDefinition tag) : base(conn)
        {
            _tag = CreateDialogTagCommand.Copy(tag);
        }
        public override string Name => "Delete dialog tag";
        public string CatalogKind => CatalogKinds.DialogTag;
        public string CatalogId => _tag.Id;
        public bool DeletesOnExecute => true;
        public bool DeletesOnUndo => false;
        protected override void ExecuteCore(DbConnection connection) => DialogCatalogRepository.DeleteTagIfUnused(connection, _tag.Id);
        protected override void UndoCore(DbConnection connection) => DialogCatalogRepository.CreateTag(connection, CreateDialogTagCommand.Copy(_tag));
    }

    public sealed class CreateDialogSnippetCommand : AuthoringCommandBase
    {
        private readonly DialogSnippetDefinition _snippet;
        public CreateDialogSnippetCommand(Func<DbConnection> conn, DialogSnippetDefinition snippet) : base(conn)
        {
            _snippet = Copy(snippet);
        }
        public override string Name => "Create dialog snippet";
        protected override void ExecuteCore(DbConnection connection) => DialogCatalogRepository.CreateSnippet(connection, Copy(_snippet));
        protected override void UndoCore(DbConnection connection) => DialogCatalogRepository.DeleteSnippet(connection, _snippet.Id);
        internal static DialogSnippetDefinition Copy(DialogSnippetDefinition snippet)
        {
            var copy = new DialogSnippetDefinition
            {
                Id = snippet?.Id ?? "", Name = snippet?.Name ?? "", Text = snippet?.Text ?? "", SortOrder = snippet?.SortOrder ?? 0
            };
            if (snippet?.DialogTagIds != null) copy.DialogTagIds.AddRange(snippet.DialogTagIds);
            return copy;
        }
    }

    public sealed class UpdateDialogSnippetCommand : AuthoringCommandBase
    {
        private readonly DialogSnippetDefinition _oldValue;
        private readonly DialogSnippetDefinition _newValue;
        public UpdateDialogSnippetCommand(Func<DbConnection> conn, DialogSnippetDefinition oldValue, DialogSnippetDefinition newValue) : base(conn)
        {
            _oldValue = CreateDialogSnippetCommand.Copy(oldValue);
            _newValue = CreateDialogSnippetCommand.Copy(newValue);
        }
        public override string Name => "Edit dialog snippet";
        protected override void ExecuteCore(DbConnection connection) => DialogCatalogRepository.UpdateSnippet(connection, CreateDialogSnippetCommand.Copy(_newValue));
        protected override void UndoCore(DbConnection connection) => DialogCatalogRepository.UpdateSnippet(connection, CreateDialogSnippetCommand.Copy(_oldValue));
    }

    public sealed class DeleteDialogSnippetCommand : AuthoringCommandBase
    {
        private readonly DialogSnippetDefinition _snippet;
        public DeleteDialogSnippetCommand(Func<DbConnection> conn, DialogSnippetDefinition snippet) : base(conn)
        {
            _snippet = CreateDialogSnippetCommand.Copy(snippet);
        }
        public override string Name => "Delete dialog snippet";
        protected override void ExecuteCore(DbConnection connection) => DialogCatalogRepository.DeleteSnippet(connection, _snippet.Id);
        protected override void UndoCore(DbConnection connection) => DialogCatalogRepository.CreateSnippet(connection, CreateDialogSnippetCommand.Copy(_snippet));
    }
}
