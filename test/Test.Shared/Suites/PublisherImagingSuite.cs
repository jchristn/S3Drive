namespace Test.Shared.Suites
{
    using System;
    using System.Buffers.Binary;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using S3Drive.Publisher;
    using S3Drive.Publisher.Imaging;
    using Test.Shared.Helpers;
    using Touchstone.Core;

    /// <summary>
    /// Tests for the Publisher's dependency-free imaging: PNG decode and encode, resampling, BMP
    /// and ICO writing, and the branding generated from the committed logo.
    /// </summary>
    public static class PublisherImagingSuite
    {
        private const string SuiteId = "PublisherImaging";

        /// <summary>
        /// Builds the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                TestCases.Create(SuiteId, "Crc32KnownValue", "CRC-32 matches the value every PNG IEND chunk carries", () =>
                {
                    byte[] iend = Encoding.ASCII.GetBytes("IEND");
                    Assert.Equal(0xAE426082u, Crc32.Compute(iend, 0, iend.Length));
                }),

                TestCases.Create(SuiteId, "PngRoundTrip", "An encoded PNG decodes back to the same pixels", () =>
                {
                    RgbaImage image = Pattern(7, 5);
                    RgbaImage decoded = PngCodec.Decode(PngCodec.Encode(image));
                    Assert.Equal(7, decoded.Width);
                    Assert.Equal(5, decoded.Height);
                    Assert.True(image.Pixels.SequenceEqual(decoded.Pixels), "pixels survive the round trip");
                }),

                TestCases.Create(SuiteId, "PngDecodesCommittedLogo", "The committed logo decodes with transparent corners and red artwork", () =>
                {
                    RgbaImage logo = PngCodec.DecodeFile(Path.Combine(RepositoryRoot(), "assets", "logo.png"));
                    Assert.Equal(337, logo.Width);
                    Assert.Equal(302, logo.Height);
                    Assert.Equal((byte)0, logo.Pixels[3], "top-left corner is transparent");
                    int red = 0;
                    for (int i = 0; i < logo.Pixels.Length; i += 4)
                    {
                        if (logo.Pixels[i + 3] > 200 && logo.Pixels[i] > 200 && logo.Pixels[i + 1] < 80 && logo.Pixels[i + 2] < 80) red++;
                    }

                    Assert.True(red > 10000, "expected the red heart, found " + red + " red pixels");
                }),

                TestCases.Create(SuiteId, "PngRejectsGarbage", "Non-PNG data fails with InvalidDataException", () =>
                {
                    Assert.Throws<InvalidDataException>(() => PngCodec.Decode(Encoding.ASCII.GetBytes("not a png at all")));
                    byte[] truncated = PngCodec.Encode(Pattern(4, 4)).Take(40).ToArray();
                    Assert.Throws<InvalidDataException>(() => PngCodec.Decode(truncated));
                }),

                TestCases.Create(SuiteId, "ResizeKeepsSolidColor", "Resampling a solid color keeps the color and the requested size", () =>
                {
                    RgbaImage solid = new RgbaImage(40, 20);
                    solid.Fill(10, 120, 250);
                    RgbaImage smaller = ImageResizer.Resize(solid, 7, 3);
                    RgbaImage larger = ImageResizer.Resize(solid, 90, 45);
                    Assert.Equal(7, smaller.Width);
                    Assert.Equal(3, smaller.Height);
                    foreach (RgbaImage image in new[] { smaller, larger })
                    {
                        for (int i = 0; i < image.Pixels.Length; i += 4)
                        {
                            Assert.Equal((byte)10, image.Pixels[i]);
                            Assert.Equal((byte)120, image.Pixels[i + 1]);
                            Assert.Equal((byte)250, image.Pixels[i + 2]);
                            Assert.Equal((byte)255, image.Pixels[i + 3]);
                        }
                    }
                }),

                TestCases.Create(SuiteId, "FitAndPadKeepAspect", "Fit keeps the aspect ratio inside the box and PadToSquare centers the image", () =>
                {
                    RgbaImage wide = new RgbaImage(200, 100);
                    RgbaImage fitted = ImageResizer.Fit(wide, 50, 50);
                    Assert.Equal(50, fitted.Width);
                    Assert.Equal(25, fitted.Height);

                    RgbaImage dot = new RgbaImage(4, 2);
                    dot.Fill(255, 0, 0);
                    RgbaImage square = dot.PadToSquare();
                    Assert.Equal(4, square.Width);
                    Assert.Equal(4, square.Height);
                    Assert.Equal((byte)0, square.Pixels[3], "padding is transparent");
                    Assert.Equal((byte)255, square.Pixels[(1 * 4 + 0) * 4 + 3], "row 1 holds the image");
                }),

                TestCases.Create(SuiteId, "BmpLayout", "BMPs are 24-bit, bottom-up, BGR, with 4-byte row padding", () =>
                {
                    RgbaImage image = new RgbaImage(3, 2);
                    image.Pixels[0] = 1;
                    image.Pixels[1] = 2;
                    image.Pixels[2] = 3;
                    byte[] bmp = BmpWriter.Encode(image);
                    int rowSize = 12;
                    Assert.Equal((byte)'B', bmp[0]);
                    Assert.Equal((byte)'M', bmp[1]);
                    Assert.Equal(54 + rowSize * 2, bmp.Length);
                    Assert.Equal(bmp.Length, BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(2, 4)));
                    Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(18, 4)));
                    Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(bmp.AsSpan(22, 4)));
                    Assert.Equal((short)24, BinaryPrimitives.ReadInt16LittleEndian(bmp.AsSpan(28, 2)));
                    int topRow = 54 + rowSize;
                    Assert.Equal((byte)3, bmp[topRow]);
                    Assert.Equal((byte)2, bmp[topRow + 1]);
                    Assert.Equal((byte)1, bmp[topRow + 2]);
                }),

                TestCases.Create(SuiteId, "IcoLayout", "ICOs carry a directory entry per frame, DIBs below 256 and a PNG at 256", () =>
                {
                    byte[] ico = IcoWriter.Encode(new List<RgbaImage> { Pattern(16, 16), Pattern(256, 256) });
                    Assert.Equal((short)1, BinaryPrimitives.ReadInt16LittleEndian(ico.AsSpan(2, 2)));
                    Assert.Equal((short)2, BinaryPrimitives.ReadInt16LittleEndian(ico.AsSpan(4, 2)));
                    Assert.Equal((byte)16, ico[6]);
                    Assert.Equal((byte)0, ico[6 + 16], "256 is stored as 0");

                    int firstOffset = BinaryPrimitives.ReadInt32LittleEndian(ico.AsSpan(6 + 12, 4));
                    Assert.Equal(40, BinaryPrimitives.ReadInt32LittleEndian(ico.AsSpan(firstOffset, 4)));
                    Assert.Equal(32, BinaryPrimitives.ReadInt32LittleEndian(ico.AsSpan(firstOffset + 8, 4)), "DIB height covers the XOR and AND masks");

                    int secondOffset = BinaryPrimitives.ReadInt32LittleEndian(ico.AsSpan(6 + 16 + 12, 4));
                    Assert.Equal((byte)0x89, ico[secondOffset]);
                    Assert.Equal((byte)'P', ico[secondOffset + 1]);

                    Assert.Throws<ArgumentException>(() => IcoWriter.Encode(new List<RgbaImage> { Pattern(16, 8) }));
                    Assert.Throws<ArgumentException>(() => IcoWriter.Encode(new List<RgbaImage>()));
                }),

                TestCases.Create(SuiteId, "BrandingFromCommittedLogo", "The committed logo yields a 10-frame icon and wizard images at four scales", () =>
                {
                    Temp.WithDir(dir =>
                    {
                        BrandingGenerator generator = new BrandingGenerator(Path.Combine(RepositoryRoot(), "assets", "logo.png"));
                        string icon = Path.Combine(dir, "s3drive.ico");
                        generator.WriteIcon(icon);
                        byte[] ico = File.ReadAllBytes(icon);
                        Assert.Equal((short)BrandingGenerator.IconSizes.Count, BinaryPrimitives.ReadInt16LittleEndian(ico.AsSpan(4, 2)));

                        WizardImageSet set = generator.WriteWizardImages(dir, "#FFFFFF");
                        Assert.Equal(4, set.LargeImages.Count);
                        Assert.Equal(4, set.SmallImages.Count);
                        byte[] large = File.ReadAllBytes(set.LargeImages[0]);
                        Assert.Equal(164, BinaryPrimitives.ReadInt32LittleEndian(large.AsSpan(18, 4)));
                        Assert.Equal(314, BinaryPrimitives.ReadInt32LittleEndian(large.AsSpan(22, 4)));
                        byte[] small = File.ReadAllBytes(set.SmallImages[0]);
                        Assert.Equal(55, BinaryPrimitives.ReadInt32LittleEndian(small.AsSpan(18, 4)));
                        Assert.Equal((byte)255, large[54], "background corner is the white wizard background");
                    });
                }),

                TestCases.Create(SuiteId, "ColorParsing", "Wizard background colors parse as #RRGGBB and reject anything else", () =>
                {
                    byte[] color = BrandingGenerator.ParseColor("#0A80fF");
                    Assert.Equal((byte)10, color[0]);
                    Assert.Equal((byte)128, color[1]);
                    Assert.Equal((byte)255, color[2]);
                    Assert.Throws<ArgumentException>(() => BrandingGenerator.ParseColor("white"));
                    Assert.Throws<ArgumentException>(() => BrandingGenerator.ParseColor("#12345G"));
                })
            };

            return new TestSuiteDescriptor(SuiteId, "S3Drive.Publisher imaging (PNG, BMP, ICO, branding)", cases);
        }

        private static RgbaImage Pattern(int width, int height)
        {
            RgbaImage image = new RgbaImage(width, height);
            for (int i = 0; i < image.Pixels.Length; i++)
            {
                image.Pixels[i] = (byte)((i * 37 + 11) % 256);
            }

            return image;
        }

        private static string RepositoryRoot()
        {
            return PublisherRunner.FindRoot(AppContext.BaseDirectory);
        }
    }
}
