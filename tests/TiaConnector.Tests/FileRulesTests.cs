using System;
using System.IO;
using TiaConnector.Contracts;
using Xunit;

namespace TiaConnector.Tests;

public sealed class FileRulesTests : IDisposable
{
    private readonly TempFolder _temp = new();

    [Theory]
    [InlineData("Estacion_FB", "Estacion_FB")]
    [InlineData("A/B:C*D", "A_B_C_D")]
    [InlineData("  ", "_")]
    public void SafeName_InvalidCharacters_ReplacesWithUnderscore(string name, string expected) =>
        Assert.Equal(expected, FileRules.SafeName(name));

    [Fact]
    public void IsPathTooLong_OverWindowsLimits_ReturnsTrue()
    {
        Assert.False(FileRules.IsPathTooLong(@"C:\a\b.xml"));
        Assert.True(FileRules.IsPathTooLong(@"C:\" + new string('a', 260)));
        Assert.True(FileRules.IsPathTooLong(@"C:\" + new string('a', 250) + @"\b.xml"));
    }

    [Fact]
    public void FreeXmlPath_NameTaken_AddsNumber()
    {
        string first = FileRules.FreeXmlPath(_temp.Path, "Bloque");
        Assert.Equal(Path.Combine(_temp.Path, "Bloque.xml"), first);
        File.WriteAllText(first, "");
        Assert.Equal(Path.Combine(_temp.Path, "Bloque_2.xml"), FileRules.FreeXmlPath(_temp.Path, "Bloque"));
    }

    [Fact]
    public void RelativeFolders_FileInsideBase_ReturnsFoldersBetween()
    {
        string file = Path.Combine(_temp.Path, "A", "B", "x.xml");
        Assert.Equal(new[] { "A", "B" }, FileRules.RelativeFolders(_temp.Path, file));
        Assert.Empty(FileRules.RelativeFolders(Path.Combine(_temp.Path, "Otra"), file));
    }

    [Fact]
    public void ReadObjectName_ExportedXml_ReturnsFirstName()
    {
        string path = Path.Combine(_temp.Path, "fb.xml");
        File.WriteAllText(path, "<Document><SW.Blocks.FB ID=\"0\"><AttributeList><Name> Ejemplo_FB </Name></AttributeList></SW.Blocks.FB></Document>");
        Assert.Equal("Ejemplo_FB", FileRules.ReadObjectName(path));
        Assert.Null(FileRules.ReadObjectName(Path.Combine(_temp.Path, "no-existe.xml")));
    }

    [Theory]
    [InlineData("Hmi.Screen.Screen", HmiBranch.Screens)]
    [InlineData("Hmi.Tag.TagTable", HmiBranch.Tags)]
    [InlineData("Hmi.TextGraphicList.TextList", HmiBranch.TextLists)]
    [InlineData("Hmi.Screen.ScreenOverview", HmiBranch.ScreenOverview)]
    public void DetectBranch_HmiElement_ReturnsBranch(string element, HmiBranch expected)
    {
        string path = Path.Combine(_temp.Path, "hmi.xml");
        File.WriteAllText(path, $"<Document><{element} ID=\"0\" /></Document>");
        Assert.Equal(expected, HmiRules.DetectBranch(path));
    }

    [Fact]
    public void DetectBranch_PlcBlock_ReturnsNull()
    {
        string path = Path.Combine(_temp.Path, "fb.xml");
        File.WriteAllText(path, "<Document><SW.Blocks.FB ID=\"0\" /></Document>");
        Assert.Null(HmiRules.DetectBranch(path));
    }

    [Fact]
    public void FoldersInsideBranch_PathWithBranchFolder_SkipsUpToIt()
    {
        Assert.Equal(new[] { "Sub" }, HmiRules.FoldersInsideBranch(HmiBranch.Screens, new[] { "HMI_1", "Pantallas", "Sub" }));
        Assert.Equal(new[] { "A", "B" }, HmiRules.FoldersInsideBranch(HmiBranch.Screens, new[] { "A", "B" }));
        Assert.Empty(HmiRules.FoldersInsideBranch(HmiBranch.TextLists, new[] { "A" }));
    }

    [Fact]
    public void HmiBranch_EnumOrder_DependenciesFirst()
    {
        Assert.True(HmiBranch.Connections < HmiBranch.Tags);
        Assert.True(HmiBranch.Tags < HmiBranch.Screens);
        Assert.True(HmiBranch.Screens < HmiBranch.ScreenOverview);
        Assert.Equal(12, HmiRules.TreeOrder.Count);
    }

    public void Dispose() => _temp.Dispose();
}
