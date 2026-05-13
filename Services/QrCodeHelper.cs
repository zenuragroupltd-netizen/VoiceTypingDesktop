using System.IO;
using System.Windows.Media.Imaging;
using QRCoder;

namespace VoiceTypingDesktop.Services;

public static class QrCodeHelper
{
    public static BitmapImage CreateQrBitmap(string text, int pixelsPerModule = 8)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
        using var png = new PngByteQRCode(data);
        var bytes = png.GetGraphic(pixelsPerModule);

        var img = new BitmapImage();
        using var ms = new MemoryStream(bytes);
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.StreamSource = ms;
        img.EndInit();
        img.Freeze();
        return img;
    }
}
