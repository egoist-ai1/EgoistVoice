using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using Forms = System.Windows.Forms;

namespace Egoist.Voice.Services;

internal static class EgoistTrayPalette
{
    internal static readonly Color Background = Color.FromArgb(5, 5, 5);
    internal static readonly Color Hover = Color.FromArgb(58, 13, 18);
    internal static readonly Color HoverBorder = Color.FromArgb(112, 24, 32);
    internal static readonly Color Primary = Color.FromArgb(247, 247, 248);
    internal static readonly Color Disabled = Color.FromArgb(112, 112, 120);
    internal static readonly Color Accent = Color.FromArgb(255, 38, 52);
    internal static readonly Color Separator = Color.FromArgb(42, 42, 48);
}

/// <summary>
/// Draws every tray-menu state explicitly so nested drop-downs never inherit
/// the Windows light renderer or its blue selection color.
/// </summary>
internal sealed class EgoistTrayRenderer : Forms.ToolStripProfessionalRenderer
{
    internal EgoistTrayRenderer()
        : base(new EgoistTrayColorTable())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(EgoistTrayPalette.Background);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(EgoistTrayPalette.Separator);
        var bounds = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        e.Graphics.DrawRectangle(pen, bounds);
    }

    protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
    {
        var bounds = new Rectangle(Point.Empty, e.Item.Size);
        using var background = new SolidBrush(e.Item.Selected
            ? EgoistTrayPalette.Hover
            : EgoistTrayPalette.Background);
        e.Graphics.FillRectangle(background, bounds);

        if (!e.Item.Selected)
        {
            return;
        }

        using var border = new Pen(EgoistTrayPalette.HoverBorder);
        e.Graphics.DrawRectangle(border, 0, 0, Math.Max(0, bounds.Width - 1), Math.Max(0, bounds.Height - 1));
    }

    protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled
            ? EgoistTrayPalette.Primary
            : EgoistTrayPalette.Disabled;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
    {
        var rect = e.ImageRectangle;
        var scale = Math.Max(1f, e.Graphics.DpiX / 96f);
        var centerX = rect.Left + (rect.Width / 2f);
        var centerY = rect.Top + (rect.Height / 2f);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(EgoistTrayPalette.Accent, 1.75f * scale)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        e.Graphics.DrawLines(pen,
        [
            new PointF(centerX - (4.5f * scale), centerY),
            new PointF(centerX - (1.2f * scale), centerY + (3.2f * scale)),
            new PointF(centerX + (5.2f * scale), centerY - (4.2f * scale))
        ]);
    }

    protected override void OnRenderArrow(Forms.ToolStripArrowRenderEventArgs e)
    {
        var scale = Math.Max(1f, e.Graphics.DpiX / 96f);
        var centerX = e.ArrowRectangle.Left + (e.ArrowRectangle.Width / 2f);
        var centerY = e.ArrowRectangle.Top + (e.ArrowRectangle.Height / 2f);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(e.Item?.Enabled != false ? EgoistTrayPalette.Primary : EgoistTrayPalette.Disabled, 1.2f * scale)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        e.Graphics.DrawLines(pen,
        [
            new PointF(centerX - (2f * scale), centerY - (3f * scale)),
            new PointF(centerX + (1f * scale), centerY),
            new PointF(centerX - (2f * scale), centerY + (3f * scale))
        ]);
    }

    protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
    {
        var y = e.Item.Height / 2;
        using var pen = new Pen(EgoistTrayPalette.Separator);
        e.Graphics.DrawLine(pen, 12, y, Math.Max(12, e.Item.Width - 12), y);
    }

    private sealed class EgoistTrayColorTable : Forms.ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => EgoistTrayPalette.Background;
        public override Color MenuItemSelected => EgoistTrayPalette.Hover;
        public override Color MenuItemBorder => EgoistTrayPalette.HoverBorder;
        public override Color MenuItemSelectedGradientBegin => EgoistTrayPalette.Hover;
        public override Color MenuItemSelectedGradientEnd => EgoistTrayPalette.Hover;
        public override Color MenuBorder => EgoistTrayPalette.Separator;
        public override Color ImageMarginGradientBegin => EgoistTrayPalette.Background;
        public override Color ImageMarginGradientMiddle => EgoistTrayPalette.Background;
        public override Color ImageMarginGradientEnd => EgoistTrayPalette.Background;
        public override Color SeparatorDark => EgoistTrayPalette.Separator;
        public override Color SeparatorLight => EgoistTrayPalette.Separator;
        public override Color CheckBackground => EgoistTrayPalette.Background;
        public override Color CheckPressedBackground => EgoistTrayPalette.Hover;
        public override Color CheckSelectedBackground => EgoistTrayPalette.Hover;
    }
}

internal static class EgoistTrayVisualPreview
{
    internal static void Render(string outputPath)
    {
        var renderer = new EgoistTrayRenderer();
        using var root = new Forms.ContextMenuStrip();
        TrayService.ConfigureDropDown(root, renderer);
        root.ShowCheckMargin = true;
        root.Items.Add(TrayService.CreateItem("Начать / остановить"));
        var activation = TrayService.CreateItem("Кнопка запуска");
        TrayService.ConfigureDropDown(activation.DropDown, renderer);
        activation.DropDownItems.Add(TrayService.CreateItem("Mouse 5"));
        root.Items.Add(activation);
        root.Items.Add(TrayService.CreateSeparator());
        root.Items.Add(TrayService.CreateItem("GigaAM + Whisper · готовы"));
        root.Items[^1].Enabled = false;
        root.Items.Add(TrayService.CreateSeparator());
        root.Items.Add(TrayService.CreateItem("Выход"));

        using var nested = new Forms.ContextMenuStrip();
        TrayService.ConfigureDropDown(nested, renderer);
        nested.ShowCheckMargin = true;
        nested.Items.Add(TrayService.CreateItem("Mouse 5 · Ctrl + Alt + Space"));
        ((Forms.ToolStripMenuItem)nested.Items[0]).Checked = true;
        nested.Items.Add(TrayService.CreateItem("Mouse 5"));
        nested.Items.Add(TrayService.CreateItem("Mouse 4"));
        nested.Items.Add(TrayService.CreateItem("Ctrl + Alt + Space"));
        nested.Items.Add(TrayService.CreateSeparator());
        nested.Items.Add(TrayService.CreateItem("Своя…  ·  Ctrl + Shift + V"));
        nested.Items.Add(TrayService.CreateItem("Недоступное действие"));
        nested.Items[^1].Enabled = false;

        root.CreateControl();
        nested.CreateControl();
        root.PerformLayout();
        nested.PerformLayout();
        root.Items[1].Select();
        nested.Items[1].Select();
        var rootSize = root.GetPreferredSize(Size.Empty);
        var nestedSize = nested.GetPreferredSize(Size.Empty);
        root.Size = rootSize;
        nested.Size = nestedSize;

        using var bitmap = new Bitmap(rootSize.Width + nestedSize.Width + 24, Math.Max(rootSize.Height, nestedSize.Height) + 24);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.FromArgb(24, 24, 26));
        }
        root.DrawToBitmap(bitmap, new Rectangle(8, 8, rootSize.Width, rootSize.Height));
        nested.DrawToBitmap(bitmap, new Rectangle(rootSize.Width + 16, 8, nestedSize.Width, nestedSize.Height));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        bitmap.Save(outputPath, ImageFormat.Png);
    }
}
