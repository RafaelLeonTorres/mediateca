using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace Mediateca.Tray;

/// <summary>Los dos íconos de la bandeja, dibujados en código (sin archivos binarios).</summary>
public sealed class TrayIcons : IDisposable
{
    /// <summary>Ícono cian (#00ACC1): la pila está en ejecución.</summary>
    public Icon Running { get; }

    /// <summary>Ícono gris (#9E9E9E): detenida, iniciando o sin Docker.</summary>
    public Icon Stopped { get; }

    /// <summary>Crea ambos íconos; se deben conservar mientras viva la app.</summary>
    public TrayIcons()
    {
        Running = Create(Color.FromArgb(0x00, 0xAC, 0xC1));
        Stopped = Create(Color.FromArgb(0x9E, 0x9E, 0x9E));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Running.Dispose();
        Stopped.Dispose();
    }

    private static Icon Create(Color color)
    {
        using var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.Clear(Color.Transparent);

            using var fill = new SolidBrush(color);
            graphics.FillEllipse(fill, 1, 1, 30, 30);

            using var font = new Font("Segoe UI", 18f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            graphics.DrawString("M", font, Brushes.White, new RectangleF(0, 0, 32, 32), format);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }
}
