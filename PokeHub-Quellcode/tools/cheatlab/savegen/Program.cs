using PKHeX.Core;
foreach (var v in new[] { GameVersion.B, GameVersion.B2 })
{
    var sav = BlankSaveFile.Get(v, "AAAAAAA", LanguageID.English);
    sav.OT = "AAAAAAA";
    var data = sav.Write(BinaryExportSetting.None).ToArray();
    var needle = System.Text.Encoding.Unicode.GetBytes("AAAAAAA");
    var hits = new List<string>();
    for (int i = 0; i < data.Length - needle.Length; i++) if (data.AsSpan(i, needle.Length).SequenceEqual(needle)) hits.Add($"0x{i:X}");
    var med = sav.Inventory.Pouches.First(p => p.Type == InventoryType.Medicine);
    Console.WriteLine($"{v}: OT name at {string.Join(",", hits)}");
}
