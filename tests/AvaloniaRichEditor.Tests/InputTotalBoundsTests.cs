using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using AvaloniaRichEditor.Formatters;
using HtmlAgilityPack;
using Xunit;

namespace AvaloniaRichEditor.Tests;

// Bounds on the SUM of what one untrusted input makes the library hold, where only each piece had one (2026-11-08).
public class InputTotalBoundsTests
{
    // Three referenced pictures of 1 MB of zeros each (a few KB deflated).
    private static MemoryStream PackageOfThreeMegabytePictures()
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            string json = "{\"Blocks\":[" + string.Join(",", new[] { "a", "b", "c" }.Select(k => $"{{\"Type\":\"Image\",\"ImageRef\":\"{k}\"}}"))
                + "],\"Images\":{\"a\":{},\"b\":{},\"c\":{}}}";
            using (var s = zip.CreateEntry("document.json").Open()) s.Write(Encoding.UTF8.GetBytes(json));
            foreach (var k in new[] { "a", "b", "c" })
                using (var s = zip.CreateEntry("images/" + k, CompressionLevel.Optimal).Open()) s.Write(new byte[1 << 20]);
        }
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void APackageWhosePicturesTogetherPassTheBound_IsRefused()
        => Assert.Throws<InvalidDataException>(() => DocumentPackage.ReadPackage(PackageOfThreeMegabytePictures(), maxPictureTotal: 2 << 20));

    [Fact]
    public void APackageWithinTheBound_ReadsEveryPicture()
    {
        var (_, pool) = DocumentPackage.ReadPackage(PackageOfThreeMegabytePictures(), maxPictureTotal: 3 << 20);
        Assert.Equal(3, pool.Count);
    }

    // A pasted page chooses how many pictures it names; all were requested at once. Port 9 on loopback refuses at once,
    // so every request fails fast and the result counts the URLs that were tried.
    [Fact]
    public async System.Threading.Tasks.Task RemotePicturePrefetch_TriesAtMostTheBound()
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(string.Concat(Enumerable.Range(0, 100).Select(i => $"<img src=\"http://127.0.0.1:9/p{i}.png\">")));
        var prefetch = typeof(HtmlDocumentFormatter).GetMethod("PrefetchRemoteImagesAsync", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = await (System.Threading.Tasks.Task<System.Collections.Generic.Dictionary<string, byte[]?>>)prefetch.Invoke(null, [doc])!;
        Assert.Equal(HtmlDocumentFormatter.MaxRemoteImages, result.Count);
    }
}
