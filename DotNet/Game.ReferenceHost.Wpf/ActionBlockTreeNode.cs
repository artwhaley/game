using System.Collections.ObjectModel;
using TruthCardGame.Content.Sqlite;

namespace TruthCardGame.ReferenceHost.Wpf
{
    /// <summary>Presentation node for the Action Blocks browser tree.</summary>
    public sealed class ActionBlockTreeNode
    {
        public ActionBlockTreeNode(string id, string name, string path,
            bool isRoot = false, bool isUnassigned = false,
            ActionBlockBrowserItem block = null)
        {
            Id = id;
            Name = name;
            Path = path ?? "";
            IsRoot = isRoot;
            IsUnassigned = isUnassigned;
            Block = block;
        }

        public string Id { get; }
        public string Name { get; }
        public string Path { get; }
        public bool IsRoot { get; }
        public bool IsUnassigned { get; }
        public ActionBlockBrowserItem Block { get; }
        public bool IsBlock => Block != null;
        public bool IsFolder => !IsBlock;
        public ActionBlockTreeNode Parent { get; internal set; }
        public ObservableCollection<ActionBlockTreeNode> Children { get; } =
            new ObservableCollection<ActionBlockTreeNode>();

        public string DisplayText => IsBlock ? Block.DisplayText : Name;

        internal void AddChild(ActionBlockTreeNode node)
        {
            node.Parent = this;
            Children.Add(node);
        }
    }
}
