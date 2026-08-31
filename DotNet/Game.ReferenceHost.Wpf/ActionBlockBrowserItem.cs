using System;
using System.Linq;
using TruthCardGame.Content.Sqlite;

namespace TruthCardGame.ReferenceHost.Wpf
{
    public sealed class ActionBlockBrowserItem
    {
        public ActionBlockBrowserItem(ActionBlockDefinition block)
        {
            Block = block ?? throw new ArgumentNullException(nameof(block));
            try
            {
                var template = ActionBlockSerializer.Deserialize(block.TemplateJson);
                Summary = string.Join(" · ", template.Actions.Select(action => action.TypeKey));
            }
            catch
            {
                Summary = "Invalid template";
            }
        }

        public ActionBlockDefinition Block { get; }
        public string Id => Block.Id;
        public string Name => Block.Name;
        public string Summary { get; }
        public string DisplayText => Name + (string.IsNullOrWhiteSpace(Summary) ? "" : "  —  " + Summary);
    }
}
