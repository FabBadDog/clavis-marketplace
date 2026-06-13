module FabioSoft.Nucleus.Plugins.Environment.Tests.PathFormattingTests

open FabioSoft.Nucleus.Plugins.Environment
open Faqt
open Faqt.Operators
open Xunit

let private home = "C:\\Users\\fhertell"

[<Fact>]
let ``collapses home and keeps four segments`` () =
    %PathFormatting.ShortPath("C:\\Users\\fhertell\\Repos\\FS\\clavis", home).Should().Be("~\\Repos\\FS\\clavis")

[<Fact>]
let ``cuts the start when more than four segments`` () =
    %PathFormatting
        .ShortPath("C:\\Users\\fhertell\\Repos\\FS\\clavis\\src\\plugins\\wpf-host\\views", home)
        .Should()
        .Be("...\\src\\plugins\\wpf-host\\views")

[<Fact>]
let ``leaves a short non-home path unchanged`` () =
    %PathFormatting.ShortPath("D:\\work\\project", home).Should().Be("D:\\work\\project")

[<Fact>]
let ``collapses home exactly to a tilde`` () =
    %PathFormatting.ShortPath("C:\\Users\\fhertell", home).Should().Be("~")

[<Fact>]
let ``leaf name is the last segment`` () =
    %PathFormatting.LeafName("C:\\Users\\fhertell\\Repos\\FS\\clavis").Should().Be("clavis")
