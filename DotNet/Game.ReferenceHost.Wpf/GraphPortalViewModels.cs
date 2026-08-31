using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace TruthCardGame.ReferenceHost.Wpf
{
    /// <summary>WPF-only matched endpoint pair for one existing logical edge.</summary>
    public sealed class GraphPortalPairViewModel
    {
        public GraphPortalPairViewModel(string id, string graphKind, string graphOwnerId, string edgeId,
            string label, int colorSlot, Point sourceLocation, Point targetLocation)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            GraphKind = graphKind ?? throw new ArgumentNullException(nameof(graphKind));
            GraphOwnerId = graphOwnerId ?? throw new ArgumentNullException(nameof(graphOwnerId));
            EdgeId = edgeId ?? throw new ArgumentNullException(nameof(edgeId));
            Label = label ?? throw new ArgumentNullException(nameof(label));
            ColorSlot = colorSlot;
            Source = new PortalEndpointViewModel(this, true, sourceLocation);
            Target = new PortalEndpointViewModel(this, false, targetLocation);
        }

        public string Id { get; }
        public string GraphKind { get; }
        public string GraphOwnerId { get; }
        public string EdgeId { get; }
        public string Label { get; }
        public int ColorSlot { get; }
        public Brush Color => GraphPresentationPalette.PortalBrush(ColorSlot);
        public PortalEndpointViewModel Source { get; }
        public PortalEndpointViewModel Target { get; }
    }

    /// <summary>Display-only Nodify decorator. It deliberately has no connection ports.</summary>
    public sealed class PortalEndpointViewModel : INotifyPropertyChanged
    {
        private Point _location;
        private bool _isSelected;

        internal PortalEndpointViewModel(GraphPortalPairViewModel pair, bool isSource, Point location)
        {
            Pair = pair;
            IsSource = isSource;
            _location = location;
        }

        public GraphPortalPairViewModel Pair { get; }
        public bool IsSource { get; }
        public string Label => Pair.Label;
        public string GraphKind => Pair.GraphKind;
        public string GraphOwnerId => Pair.GraphOwnerId;
        public string EdgeId => Pair.EdgeId;
        public Brush Color => Pair.Color;
        public string DirectionLabel => IsSource ? "P" + Label.TrimStart('P') + "  >" : ">  P" + Label.TrimStart('P');

        /// <summary>Top-left graph coordinate of the 64x30 endpoint decorator.</summary>
        public Point Location
        {
            get => _location;
            set
            {
                if (_location == value) return;
                _location = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Location)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Anchor)));
            }
        }

        public Point Anchor => new Point(Location.X + 32, Location.Y + 15);

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
    }
}
