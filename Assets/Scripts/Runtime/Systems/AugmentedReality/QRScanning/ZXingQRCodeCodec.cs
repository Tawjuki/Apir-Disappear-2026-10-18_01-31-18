using System;
using System.Collections.Generic;
using UnityEngine;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

namespace ApirApir.AugmentedReality
{
    /// <summary>Maintained ZXing adapter for printable QR generation and camera-frame decoding.</summary>
    public sealed class ZXingQRCodeCodec
    {
        public const int MinimumTextureSize = 256;

        private readonly QRCodeService payloads = new QRCodeService();
        private readonly BarcodeReader reader;

        public ZXingQRCodeCodec()
        {
            reader = new BarcodeReader
            {
                AutoRotate = true,
                Options = new DecodingOptions
                {
                    TryHarder = true,
                    PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE }
                }
            };
        }

        public Texture2D CreateCardIdTexture(string cardId, int size = 512)
        {
            int dimension = Mathf.Max(MinimumTextureSize, size);
            string payload = payloads.CreatePayload(cardId);
            var writer = new BarcodeWriter
            {
                Format = BarcodeFormat.QR_CODE,
                Options = new QrCodeEncodingOptions
                {
                    Width = dimension,
                    Height = dimension,
                    Margin = 4,
                    ErrorCorrection = ErrorCorrectionLevel.M,
                    CharacterSet = "UTF-8"
                }
            };
            Color32[] pixels = writer.Write(payload);
            var texture = new Texture2D(dimension, dimension, TextureFormat.RGBA32, false, true)
            {
                name = "APIR_AR_QR_" + cardId,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        public bool TryDecode(Color32[] pixels, int width, int height, out string cardId)
        {
            cardId = string.Empty;
            if (pixels == null || width <= 0 || height <= 0 || pixels.Length != width * height)
                return false;
            try
            {
                Result result = reader.Decode(pixels, width, height);
                return result != null && payloads.TryParsePayload(result.Text, out cardId);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool TryDecodeRgba32(byte[] rgbaBytes, int width, int height, out string cardId)
        {
            cardId = string.Empty;
            if (rgbaBytes == null || width <= 0 || height <= 0 || rgbaBytes.Length != width * height * 4)
                return false;
            try
            {
                var source = new RGBLuminanceSource(
                    rgbaBytes, width, height, RGBLuminanceSource.BitmapFormat.RGBA32);
                var bitmap = new BinaryBitmap(new HybridBinarizer(source));
                Result result = new MultiFormatReader().decode(bitmap, reader.Options.Hints);
                return result != null && payloads.TryParsePayload(result.Text, out cardId);
            }
            catch (ReaderException)
            {
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
