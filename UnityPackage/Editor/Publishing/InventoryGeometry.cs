using System;

namespace RimUnity.Editor
{
    //Derived only from window bounds, never from GUILayout's Layout/Repaint cache.
    internal sealed class InventoryGeometry
    {
        internal readonly struct Box
        {
            internal readonly float x, y, width, height;
            internal float Right => x + width;
            internal float Bottom => y + height;
            internal Box(float x, float y, float width, float height)
            { this.x = x; this.y = y; this.width = Math.Max(1, width); this.height = Math.Max(1, height); }
        }
        internal Box Content, Footer, Error, Sidebar, Main, Search, Table, Selection, BundleList, BundleDetails;

        internal static InventoryGeometry Calculate(float width, float height, bool error = false)
        {
            var layout = new InventoryGeometry();
            float top = error ? 136 : 86;
            layout.Error = new Box(8, 86, width - 16, 42);
            layout.Content = new Box(8, top, width - 16, height - top - 66);
            layout.Footer = new Box(0, height - 58, width, 58);
            float sidebar = Math.Max(196, Math.Min(244, width * .22f));
            layout.Sidebar = new Box(8, top, sidebar, layout.Content.height);
            layout.Main = new Box(layout.Sidebar.Right + 8, top, layout.Content.width - sidebar - 8, layout.Content.height);
            layout.Search = new Box(layout.Main.x + 8, top + 8, layout.Main.width - 16, 32);
            float selection = layout.Main.width < 720 ? 158 : 112;
            layout.Selection = new Box(layout.Main.x, layout.Main.Bottom - selection, layout.Main.width, selection);
            layout.Table = new Box(layout.Main.x, top + 48, layout.Main.width, layout.Selection.y - top - 56);
            layout.BundleDetails = new Box(8, layout.Sidebar.Bottom - 166, sidebar, 166);
            layout.BundleList = new Box(8, top + 80, sidebar, layout.BundleDetails.y - top - 88);
            return layout;
        }
    }
}
