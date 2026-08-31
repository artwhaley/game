using System.Collections.ObjectModel;
using System.ComponentModel;
using TruthCardGame.Content;

namespace TruthCardGame.ReferenceHost.Wpf
{
    /// <summary>Presentation node for the Cards pane's persisted folder hierarchy.</summary>
    public sealed class CardFolderTreeNode : INotifyPropertyChanged
    {
        public CardFolderTreeNode(string id, string name, string path, bool isAllCards = false,
            bool isUnassigned = false, CardDefinition card = null)
        {
            Id = id;
            Name = name;
            Path = path ?? "";
            IsAllCards = isAllCards;
            IsUnassigned = isUnassigned;
            Card = card;
        }

        public string Id { get; }
        public string Name { get; }
        public string Path { get; }
        public bool IsAllCards { get; }
        public bool IsUnassigned { get; }
        public CardDefinition Card { get; }
        public bool IsCard => Card != null;
        public bool IsFolder => !IsCard && !IsAllCards && !IsUnassigned;
        public CardFolderTreeNode Parent { get; internal set; }
        public ObservableCollection<CardFolderTreeNode> Children { get; } = new ObservableCollection<CardFolderTreeNode>();

        /// <summary>Recursive count of cards in this folder and its descendants;
        /// recomputed as cards are appended during tree construction.</summary>
        public int CardCount { get; private set; }

        internal void AddChild(CardFolderTreeNode node)
        {
            node.Parent = this;
            Children.Add(node);
            if (node.IsCard)
            {
                var owner = this;
                while (owner != null)
                {
                    owner.CardCount++;
                    owner = owner.Parent;
                }
            }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public override string ToString() => Name;
    }
}
