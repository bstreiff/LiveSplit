using LiveSplit.Options;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;
using static System.Windows.Forms.TextRenderer;

namespace LiveSplit.UI
{
    public class SimpleLabel
    {
        private static bool DXEnabled { get; set; }
        public string Text { get; set; }
        public ICollection<string> AlternateText { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public Font Font { get; set; }
        private SharpDX.DirectWrite.Font DXFont { get; set; }
        private SharpDX.DirectWrite.TextFormat DXTextFormat { get; set; }
        public Brush Brush { get; set; }
        public StringAlignment HorizontalAlignment { get; set; }
        public StringAlignment VerticalAlignment { get; set; }
        public Color ShadowColor { get; set; }
        public Color OutlineColor { get; set; }

        public bool HasShadow { get; set; }
        public bool IsMonospaced { get; set; }

        private StringFormat Format { get; set; }

        public float ActualWidth { get; set; }

        public Color ForeColor
        {
            get
            {
                return ((SolidBrush)Brush).Color;
            }
            set
            {
                try
                {
                    if (Brush is SolidBrush)
                    {
                        ((SolidBrush)Brush).Color = value;
                    }
                    else
                    {
                        Brush = new SolidBrush(value);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                }
            }
        }

        private static SharpDX.Direct2D1.Factory FactoryD2D1 { get; set; }
        private static SharpDX.DirectWrite.Factory FactoryDWrite { get; set; }
        private static SharpDX.DirectWrite.GdiInterop GdiInterop { get; set; }
        private SharpDX.Direct2D1.DeviceContextRenderTarget DXRenderTarget { get; set; }
        private Graphics LastGraphics { get; set; }
        private Brush LastBrush { get; set; }
        private Font LastFont { get; set; }
        private SharpDX.Direct2D1.Brush DXBrush { get; set; }

        private static void InitDirectWrite()
        {
            if (FactoryDWrite == null)
            {
                FactoryD2D1 = new SharpDX.Direct2D1.Factory();
                FactoryDWrite = new SharpDX.DirectWrite.Factory();
                GdiInterop = FactoryDWrite.GdiInterop;
                DXEnabled = true;

                SharpDX.Configuration.EnableObjectTracking = true;
            }
        }

        public SimpleLabel(
            string text = "",
            float x = 0.0f, float y = 0.0f,
            Font font = null, Brush brush = null,
            float width = float.MaxValue, float height = float.MaxValue,
            StringAlignment horizontalAlignment = StringAlignment.Near,
            StringAlignment verticalAlignment = StringAlignment.Near,
            IEnumerable<string> alternateText = null)
        {
            InitDirectWrite();
            Text = text;
            X = x;
            Y = y;
            Font = font ?? new Font("Arial", 1.0f);
            Brush = brush ?? new SolidBrush(Color.Black);
            Width = width;
            Height = height;
            HorizontalAlignment = horizontalAlignment;
            VerticalAlignment = verticalAlignment;
            IsMonospaced = false;
            HasShadow = true;
            ShadowColor = Color.FromArgb(128, 0, 0, 0);
            OutlineColor = Color.FromArgb(0, 0, 0, 0);
            ((List<string>)(AlternateText = new List<string>())).AddRange(alternateText ?? new string[0]);
            Format = new StringFormat
            {
                Alignment = HorizontalAlignment,
                LineAlignment = VerticalAlignment,
                FormatFlags = StringFormatFlags.NoWrap,
                Trimming = StringTrimming.EllipsisCharacter
            };

            if (DXEnabled)
            {
                var props = new SharpDX.Direct2D1.RenderTargetProperties(
                    SharpDX.Direct2D1.RenderTargetType.Default,
                    new SharpDX.Direct2D1.PixelFormat(SharpDX.DXGI.Format.B8G8R8A8_UNorm, SharpDX.Direct2D1.AlphaMode.Premultiplied),
                    0, 0,
                    SharpDX.Direct2D1.RenderTargetUsage.GdiCompatible,
                    SharpDX.Direct2D1.FeatureLevel.Level_DEFAULT);

                DXRenderTarget = new SharpDX.Direct2D1.DeviceContextRenderTarget(FactoryD2D1, props);
            }
        }

        public void Draw(Graphics g)
        {
            Format.Alignment = HorizontalAlignment;
            Format.LineAlignment = VerticalAlignment;

            if (!IsMonospaced)
            {
                var actualText = CalculateAlternateText(g, Width);
                DrawTextDW(actualText, g, X, Y, Width, Height, Format);
            }
            else
            {
                var monoFormat = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = VerticalAlignment
                };

                var measurement = MeasureText(g, "0", Font, new Size((int)(Width + 0.5f), (int)(Height + 0.5f)), TextFormatFlags.NoPadding).Width;
                var offset = Width;
                var charIndex = 0;
                SetActualWidth(g);
                var cutOffText = CutOff(g);

                offset = Width - MeasureActualWidth(cutOffText, g);
                if (HorizontalAlignment != StringAlignment.Far)
                    offset = 0f;


                while (charIndex < cutOffText.Length)
                {
                    var curOffset = 0f;
                    var curChar = cutOffText[charIndex];

                    if (char.IsDigit(curChar))
                        curOffset = measurement;
                    else
                        curOffset = MeasureText(g, curChar.ToString(), Font, new Size((int)(Width + 0.5f), (int)(Height + 0.5f)), TextFormatFlags.NoPadding).Width;

                    DrawTextDW(curChar.ToString(), g, X + offset - curOffset / 2f, Y, curOffset * 2f, Height, monoFormat);

                    charIndex++;
                    offset += curOffset;
                }
            }
        }

        private SharpDX.Mathematics.Interop.RawMatrix3x2 SDMatrixToDXMatrix(Matrix mat)
        {
            var elms = mat.Elements;
            return new SharpDX.Mathematics.Interop.RawMatrix3x2(elms[0], elms[1], elms[2], elms[3], elms[4], elms[5]);
        }

        private SharpDX.Color4 SDColorToDXColor4(System.Drawing.Color color)
        {
            return new SharpDX.Color4(
                    (float)color.R / 255.0f, (float)color.G / 255.0f, (float)color.B / 255.0f, (float)color.A / 255.0f);
        }

        private SharpDX.Direct2D1.Brush ToDXBrush(SharpDX.Direct2D1.RenderTarget renderTarget)
        {
            if (Brush is SolidBrush)
            {
                var color = ((SolidBrush)Brush).Color;
                return new SharpDX.Direct2D1.SolidColorBrush(renderTarget, SDColorToDXColor4(color));
            }
            else if (Brush is LinearGradientBrush)
            {
                var lgb = (LinearGradientBrush)Brush;
                // Only handle the PointF/PointF/Color/Color case

                var gradientStops = new SharpDX.Direct2D1.GradientStop[] {
                    new SharpDX.Direct2D1.GradientStop {
                        Color = SDColorToDXColor4(lgb.LinearColors[0]), Position = 0.0f
                    },
                    new SharpDX.Direct2D1.GradientStop {
                        Color = SDColorToDXColor4(lgb.LinearColors[1]), Position = 1.0f
                    }
                };

                // Note: These are absolute coordinates, they are not relative to the render target, aaaaaah
                // This means we need to reverse the start/end points from the LGB's transformation matrix,
                // and apply them to the render target's coordinate space...
                var gradientProps = new SharpDX.Direct2D1.LinearGradientBrushProperties
                {
                    StartPoint = new SharpDX.Vector2(0, 0),
                    EndPoint = new SharpDX.Vector2(renderTarget.PixelSize.Width, renderTarget.PixelSize.Height),
                };

                var gradientStopCollection = new SharpDX.Direct2D1.GradientStopCollection(renderTarget, gradientStops);
                // need to compute this out...

                var dxBrush = new SharpDX.Direct2D1.LinearGradientBrush(renderTarget, gradientProps, gradientStopCollection);
                gradientStopCollection.Dispose();
                return dxBrush;
            }

            // last resort;
            return new SharpDX.Direct2D1.SolidColorBrush(renderTarget, new SharpDX.Color4(1.0f, 1.0f, 0.0f, 1.0f));
        }

        private SharpDX.Rectangle DXRectFToRect(SharpDX.RectangleF x)
        {
            return new SharpDX.Rectangle((int)x.Left, (int)x.Top, (int)x.Right, (int)x.Bottom);
        }

        // TODO: these are variables so I can switch them from the debugger
        private bool ShowBorders = false;
        private bool ShowGDIText = false;

        private void DrawTextDW(string text, Graphics g, float x, float y, float width, float height, StringFormat format)
        {

            if (ShowGDIText)
                DrawText(text, g, x, y, width, height, format);

            if (text != null)
            {
                // For StringFormat fmt, we only need to look at
                // Alignment (horiz alignment)
                // LineAlignment (vertical alignment)
                // FormatFlags is always StringFormatFlags.NoWrap
                // Trimming is always StringTrimming.EllipsisCharacter

                if (g != LastGraphics)
                {
                    LastGraphics = g;
                    // force recreation of these too
                    LastBrush = null;
                    LastFont = null;
                }

                if (!object.ReferenceEquals(Font, LastFont))
                {
                    var logfont = new SharpDX.DirectWrite.GdiInterop.LogFont();
                    Font.ToLogFont(logfont);

                    if (DXFont != null)
                        DXFont.Dispose();

                    DXFont = GdiInterop.FromLogFont(logfont);

                    if (DXTextFormat != null)
                        DXTextFormat.Dispose();

                    DXTextFormat = new SharpDX.DirectWrite.TextFormat(FactoryDWrite, Font.FontFamily.Name, DXFont.Weight, DXFont.Style, DXFont.Stretch, GetFontSize(g));
                    DXTextFormat.WordWrapping = SharpDX.DirectWrite.WordWrapping.NoWrap;
                    // TODO: trimming
                }

                switch (format.LineAlignment)
                {
                    case StringAlignment.Center:
                        DXTextFormat.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center;
                        break;
                    case StringAlignment.Far:
                        DXTextFormat.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Far;
                        break;
                    case StringAlignment.Near:
                        DXTextFormat.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Near;
                        break;
                }
                switch (format.Alignment)
                {
                    case StringAlignment.Center:
                        DXTextFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Center;
                        break;
                    case StringAlignment.Far:
                        DXTextFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Trailing;
                        break;
                    case StringAlignment.Near:
                        DXTextFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
                        break;
                }

                var layout = new SharpDX.DirectWrite.TextLayout(FactoryDWrite, text, DXTextFormat, width, height);
                // layout.Metrics has size info

                var matrix = SDMatrixToDXMatrix(g.Transform);
                var topLeft = SharpDX.Matrix3x2.TransformPoint(matrix, new SharpDX.Vector2(x, y));
                var bottomRight = SharpDX.Matrix3x2.TransformPoint(matrix, new SharpDX.Vector2(x + width, y + height));

                var targetArea = new SharpDX.RectangleF(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
                var gdiSubRect = DXRectFToRect(targetArea);
                var bounds = new SharpDX.RectangleF(0, 0, width, height);
                var dxTransform = matrix;
                //dxTransform.M31 += gdiSubRect.Left;
                dxTransform.M32 -= gdiSubRect.Top;

                if (!object.ReferenceEquals(Brush, LastBrush))
                {
                    if (DXBrush != null)
                        DXBrush.Dispose();

                    DXBrush = ToDXBrush(DXRenderTarget);
                    LastBrush = Brush;
                }

                // DEBUG: This is the bounding box where we /should/ be
                if (ShowBorders)
                    g.DrawRectangle(new Pen(Color.FromArgb(255, 0, 255, 0)), x, y, width, height);

                IntPtr hdc = g.GetHdc();
                DXRenderTarget.BindDeviceContext(hdc, gdiSubRect);
                DXRenderTarget.BeginDraw();
                DXRenderTarget.Transform = dxTransform;
                if (ShowBorders)
                {
                    var tmpBrush = new SharpDX.Direct2D1.SolidColorBrush(DXRenderTarget, new SharpDX.Color4(1.0f, 0.0f, 0.0f, 1.0f));
                    DXRenderTarget.DrawRectangle(bounds, tmpBrush);
                    tmpBrush.Dispose();
                }

                //dcrt.DrawText(text, textFormat, bounds, ToDXBrush(dcrt), SharpDX.Direct2D1.DrawTextOptions.EnableColorFont);
                DXRenderTarget.DrawTextLayout(new SharpDX.Vector2(0, 0), layout, DXBrush, SharpDX.Direct2D1.DrawTextOptions.EnableColorFont);

                //var geometry = new SharpDX.Direct2D1.PathGeometry(FactoryD2D1);
                //var geometrySink = geometry.Open();
                //var fontFace = new SharpDX.DirectWrite.FontFace(DXFont);
                //fontFace.GetGlyphRunOutline(...)

                // TODO: in order to support outlines, we need to use DirectWrite to generate us some geometry?
                // Draw an outline
                // DXRenderTarget.DrawGeometry(geometry, brush);
                // Draw text body
                // DXRenderTarget.FillGeometry(geometry, fillbrush);

                DXRenderTarget.EndDraw();
                g.ReleaseHdc(hdc);

                layout.Dispose();
            }

        }

        private void DrawText(string text, Graphics g, float x, float y, float width, float height, StringFormat format)
        {
            if (text != null)
            {
                if (g.TextRenderingHint == TextRenderingHint.AntiAlias && OutlineColor.A > 0)
                {
                    var fontSize = GetFontSize(g);
                    using (var shadowBrush = new SolidBrush(ShadowColor))
                    using (var gp = new GraphicsPath())
                    using (var outline = new Pen(OutlineColor, GetOutlineSize(fontSize)) { LineJoin = LineJoin.Round })
                    {
                        if (HasShadow)
                        {
                            gp.AddString(text, Font.FontFamily, (int)Font.Style, fontSize, new RectangleF(x + 1f, y + 1f, width, height), format);
                            g.FillPath(shadowBrush, gp);
                            gp.Reset();
                            gp.AddString(text, Font.FontFamily, (int)Font.Style, fontSize, new RectangleF(x + 2f, y + 2f, width, height), format);
                            g.FillPath(shadowBrush, gp);
                            gp.Reset();
                        }
                        gp.AddString(text, Font.FontFamily, (int)Font.Style, fontSize, new RectangleF(x, y, width, height), format);
                        g.DrawPath(outline, gp);
                        g.FillPath(Brush, gp);
                    }
                }
                else
                {
                    if (HasShadow)
                    {
                        using (var shadowBrush = new SolidBrush(ShadowColor))
                        {
                            g.DrawString(text, Font, shadowBrush, new RectangleF(x + 1f, y + 1f, width, height), format);
                            g.DrawString(text, Font, shadowBrush, new RectangleF(x + 2f, y + 2f, width, height), format);
                        }
                    }
                    g.DrawString(text, Font, Brush, new RectangleF(x, y, width, height), format);
                }
            }
        }

        private float GetOutlineSize(float fontSize)
        {
            return 2.1f + fontSize * 0.055f;
        }

        private float GetFontSize(Graphics g)
        {
            if (Font.Unit == GraphicsUnit.Point)
                return Font.Size * g.DpiY / 72;
            return Font.Size;
        }

        public void SetActualWidth(Graphics g)
        {
            Format.Alignment = HorizontalAlignment;
            Format.LineAlignment = VerticalAlignment;

            if (!IsMonospaced)
                ActualWidth = g.MeasureString(Text, Font, 9999, Format).Width;
            else
                ActualWidth = MeasureActualWidth(Text, g);
        }

        public string CalculateAlternateText(Graphics g, float width)
        {
            var actualText = Text;
            ActualWidth = g.MeasureString(Text, Font, 9999, Format).Width;
            foreach (var curText in AlternateText.OrderByDescending(x => x.Length))
            {
                if (width < ActualWidth)
                {
                    actualText = curText;
                    ActualWidth = g.MeasureString(actualText, Font, 9999, Format).Width;
                }
                else
                {
                    break;
                }
            }
            return actualText;
        }

        private float MeasureActualWidth(string text, Graphics g)
        {
            var charIndex = 0;
            var measurement = MeasureText(g, "0", Font, new Size((int)(Width + 0.5f), (int)(Height + 0.5f)), TextFormatFlags.NoPadding).Width;
            var offset = 0;

            while (charIndex < text.Length)
            {
                var curChar = text[charIndex];

                if (char.IsDigit(curChar))
                    offset += measurement;
                else
                    offset += MeasureText(g, curChar.ToString(), Font, new Size((int)(Width + 0.5f), (int)(Height + 0.5f)), TextFormatFlags.NoPadding).Width;

                charIndex++;
            }
            return offset;
        }

        private string CutOff(Graphics g)
        {
            if (ActualWidth < Width)
                return Text;
            var cutOffText = Text;
            while (ActualWidth >= Width && !string.IsNullOrEmpty(cutOffText))
            {
                cutOffText = cutOffText.Remove(cutOffText.Length - 1, 1);
                ActualWidth = MeasureActualWidth(cutOffText + "...", g);
            }
            if (ActualWidth >= Width)
                return "";
            return cutOffText + "...";
        }
    }
}
