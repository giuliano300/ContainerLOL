using SharedLib.Models;
namespace SharedLib.Utils;
public static class RecipientDocumentStorage
{
    private static readonly string Root = Environment.GetEnvironmentVariable("EASYSENDER_RECIPIENT_FILES_ROOT") ?? @"B:\EasySenderFiles";
    public static byte[]? Read(Recipients r, string t) { var p = Path.Combine(Root,"recipients",r.Id.ToString(), Name(t)); return File.Exists(p) ? File.ReadAllBytes(p) : null; }
    public static void WriteFinal(Recipients r, byte[] c) { var d=Path.Combine(Root,"recipients",r.Id.ToString()); Directory.CreateDirectory(d); var p=Path.Combine(d,Name("ra")); File.WriteAllBytes(p+".uploading",c); File.Move(p+".uploading",p,true); r.AttachedFileRaName=$"RA-{r.FileName ?? r.Id+".pdf"}"; }
    public static bool HasFinal(Recipients r) => !string.IsNullOrWhiteSpace(r.AttachedFileRaName);
    private static string Name(string t) => t switch { "source"=>"source.bin", "rr"=>"return-receipt.bin", "ra"=>"final-document.bin", _=>throw new ArgumentOutOfRangeException(nameof(t))};
}
